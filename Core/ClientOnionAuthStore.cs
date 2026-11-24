using System.Security.AccessControl;
using System.Security.Principal;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace OrbotPc.Core;

/// <summary>Stores Tor v3 client authorization keys as native .auth_private files.</summary>
public sealed class ClientOnionAuthStore
{
    private static readonly Regex DomainPattern = new("^[a-z2-7]{56}\\.onion$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex KeyPattern = new("^[a-z2-7]{52}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private readonly bool _requirePrivateAcl;

    public string DirectoryPath { get; }

    public ClientOnionAuthStore(string directoryPath, bool requirePrivateAcl = true)
    {
        if (string.IsNullOrWhiteSpace(directoryPath)) throw new ArgumentException("An authorization directory is required.", nameof(directoryPath));
        DirectoryPath = Path.GetFullPath(directoryPath);
        _requirePrivateAcl = requirePrivateAcl;
        Directory.CreateDirectory(DirectoryPath);
        try { RestrictDirectoryToCurrentUser(DirectoryPath); }
        catch (UnauthorizedAccessException) when (!requirePrivateAcl)
        {
            // Test hosts may not be allowed to rewrite a temporary directory ACL.
        }
    }

    public void Add(string onionDomain, string privateKey)
    {
        var domain = NormalizeDomain(onionDomain);
        var key = NormalizeKey(privateKey);
        var destination = Path.Combine(DirectoryPath, domain + ".auth_private");
        var temporary = Path.Combine(DirectoryPath, "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (File.Create(temporary)) { }
            try { RestrictFileToCurrentUser(temporary); }
            catch (UnauthorizedAccessException)
            {
                if (_requirePrivateAcl) throw;
            }
            File.WriteAllText(temporary, $"{domain}:descriptor:x25519:{key}", new UTF8Encoding(false));
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporary); } catch (IOException) { }
        }
    }

    public string Import(string authPrivateFileContent)
    {
        if (string.IsNullOrWhiteSpace(authPrivateFileContent)) throw new ArgumentException("The authorization file is empty.", nameof(authPrivateFileContent));
        if (authPrivateFileContent.Length > 4096) throw new ArgumentException("The authorization file exceeds 4 KiB.", nameof(authPrivateFileContent));
        var content = authPrivateFileContent.Trim().TrimStart('\uFEFF');
        if (content.Contains('\n') || content.Contains('\r') || content.Contains('\0'))
            throw new ArgumentException("The authorization file must contain one .auth_private entry.", nameof(authPrivateFileContent));
        var parts = content.Split(':');
        if (parts.Length != 4 || !string.Equals(parts[1], "descriptor", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(parts[2], "x25519", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Expected Tor v3 client authorization format: domain.onion:descriptor:x25519:KEY.", nameof(authPrivateFileContent));
        var domain = NormalizeDomain(parts[0]);
        Add(domain, parts[3]);
        return domain;
    }

    public bool Remove(string onionDomain)
    {
        var domain = NormalizeDomain(onionDomain);
        var path = Path.Combine(DirectoryPath, domain + ".auth_private");
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    /// <summary>Reads one validated native .auth_private entry for export.</summary>
    public string Read(string onionDomain)
    {
        var domain = NormalizeDomain(onionDomain);
        var path = Path.Combine(DirectoryPath, domain + ".auth_private");
        if (!File.Exists(path)) throw new FileNotFoundException("The selected authorization was not found.", path);
        var content = File.ReadAllText(path, Encoding.UTF8).Trim().TrimStart('\uFEFF');
        var prefix = $"{domain}:descriptor:x25519:";
        if (!content.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The selected authorization file is invalid.");
        NormalizeKey(content[prefix.Length..]);
        return content;
    }

    public IReadOnlyList<string> ListDomains()
    {
        var domains = new List<string>();
        foreach (var path in Directory.EnumerateFiles(DirectoryPath, "*.auth_private", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var content = File.ReadAllText(path, Encoding.UTF8).Trim().TrimStart('\uFEFF');
                var separator = content.IndexOf(':');
                if (separator <= 0) continue;
                var domain = NormalizeDomain(content[..separator]);
                if (string.Equals(Path.GetFileName(path), domain + ".auth_private", StringComparison.OrdinalIgnoreCase))
                    domains.Add(domain);
            }
            catch (ArgumentException) { }
        }
        return domains.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static string NormalizeDomain(string onionDomain)
    {
        var domain = (onionDomain ?? string.Empty).Trim().TrimEnd('.').ToLowerInvariant();
        if (!domain.EndsWith(".onion", StringComparison.Ordinal)) domain += ".onion";
        if (!DomainPattern.IsMatch(domain)) throw new ArgumentException("A v3 onion domain must contain 56 base32 characters followed by .onion.", nameof(onionDomain));
        return domain;
    }

    private static string NormalizeKey(string privateKey)
    {
        var key = (privateKey ?? string.Empty).Trim().ToLowerInvariant();
        if (!KeyPattern.IsMatch(key) || key[^1] is not ('a' or 'q'))
            throw new ArgumentException("A v3 x25519 private key must be 52 base32 characters with a valid final padding character.", nameof(privateKey));
        return key;
    }

    private static void RestrictDirectoryToCurrentUser(string directoryPath)
    {
        if (!OperatingSystem.IsWindows()) return;
        var identity = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Could not determine the current Windows user for authorization-key protection.");
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(identity);
        security.AddAccessRule(new FileSystemAccessRule(
            identity,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        new DirectoryInfo(directoryPath).SetAccessControl(security);
    }

    private static void RestrictFileToCurrentUser(string filePath)
    {
        if (!OperatingSystem.IsWindows()) return;
        var identity = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Could not determine the current Windows user for authorization-key protection.");
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(identity);
        security.AddAccessRule(new FileSystemAccessRule(identity, FileSystemRights.FullControl, AccessControlType.Allow));
        new FileInfo(filePath).SetAccessControl(security);
    }
}
