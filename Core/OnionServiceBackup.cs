using System.IO.Compression;
using System.IO;
using System.Text;
using System.Text.Json;

namespace OrbotPc.Core;

/// <summary>
/// Portable backup for the Onion Service configuration represented by the PC UI.
/// Tor's private service keys stay in the local data directory and are intentionally
/// never copied into this archive.
/// </summary>
public static class OnionServiceBackup
{
    private const string ManifestName = "manifest.json";
    private const string FormatName = "orbot-pc-onion-services";
    private const int FormatVersion = 1;
    private const int MaxServices = 128;
    private const long MaxArchiveBytes = 2 * 1024 * 1024;
    private const long MaxManifestBytes = 512 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void Export(string archivePath, IEnumerable<OnionServiceEntry> services)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentNullException.ThrowIfNull(services);

        var list = ValidateAndClone(services);
        var fullPath = Path.GetFullPath(archivePath);
        var parent = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(parent)) throw new ArgumentException("Backup path must include a directory.", nameof(archivePath));
        Directory.CreateDirectory(parent);

        var temporaryPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var file = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: false))
            {
                var entry = archive.CreateEntry(ManifestName, CompressionLevel.Optimal);
                using var stream = entry.Open();
                JsonSerializer.Serialize(stream, new BackupDocument(FormatName, FormatVersion, list), JsonOptions);
            }

            var size = new FileInfo(temporaryPath).Length;
            if (size > MaxArchiveBytes) throw new InvalidDataException("Onion service backup is too large.");
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch { }
        }
    }

    public static IReadOnlyList<OnionServiceEntry> Import(string archivePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        var fullPath = Path.GetFullPath(archivePath);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("Onion service backup was not found.", fullPath);
        if (new FileInfo(fullPath).Length > MaxArchiveBytes) throw new InvalidDataException("Onion service backup is too large.");

        using var file = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: false);
        if (archive.Entries.Count != 1) throw new InvalidDataException("Backup contains unexpected files.");
        var entry = archive.Entries[0];
        if (!string.Equals(entry.FullName, ManifestName, StringComparison.Ordinal) || entry.Length > MaxManifestBytes)
            throw new InvalidDataException("Backup contains an invalid manifest entry.");

        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var json = reader.ReadToEnd();
        BackupDocument? document;
        try { document = JsonSerializer.Deserialize<BackupDocument>(json, JsonOptions); }
        catch (JsonException ex) { throw new InvalidDataException("Backup manifest is not valid JSON.", ex); }
        if (document is null || !string.Equals(document.Format, FormatName, StringComparison.Ordinal) || document.Version != FormatVersion)
            throw new InvalidDataException("Backup format is unsupported.");
        return ValidateAndClone(document.Services ?? throw new InvalidDataException("Backup has no service list."));
    }

    private static List<OnionServiceEntry> ValidateAndClone(IEnumerable<OnionServiceEntry> services)
    {
        var list = services.ToList();
        if (list.Count > MaxServices) throw new InvalidDataException($"A backup can contain at most {MaxServices} Onion services.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var safeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<OnionServiceEntry>(list.Count);
        foreach (var service in list)
        {
            if (service is null) throw new InvalidDataException("Backup contains a null Onion service.");
            var name = service.Name?.Trim() ?? string.Empty;
            if (name.Length is 0 or > 128 || name.Any(c => char.IsControl(c) || c is '\r' or '\n' or '\0'))
                throw new InvalidDataException("Onion service name is empty or invalid.");
            if (!names.Add(name) || !safeNames.Add(SafeName(name)))
                throw new InvalidDataException("Onion service names must be unique.");
            if (service.VirtualPort is < 1 or > 65535 || service.TargetPort is < 1 or > 65535)
                throw new InvalidDataException("Onion service ports must be between 1 and 65535.");
            result.Add(new OnionServiceEntry { Name = name, VirtualPort = service.VirtualPort, TargetPort = service.TargetPort });
        }
        return result;
    }

    private static string SafeName(string name)
    {
        var normalized = new string(name.Trim().ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? "service" : normalized[..Math.Min(normalized.Length, 48)];
    }

    private sealed record BackupDocument(string Format, int Version, List<OnionServiceEntry> Services);
}
