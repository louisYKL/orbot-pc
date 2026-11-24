using System.IO.Compression;
using System.Text;
using OrbotPc.Core;

namespace OrbotPc.Tests;

public static class OnionServiceBackupTests
{
    public static void Run(string root)
    {
        Directory.CreateDirectory(root);
        var archive = Path.Combine(root, "services.zip");
        var source = new[]
        {
            new OnionServiceEntry { Name = "Web", VirtualPort = 80, TargetPort = 8080 },
            new OnionServiceEntry { Name = "admin-api", VirtualPort = 443, TargetPort = 9443 }
        };
        OnionServiceBackup.Export(archive, source);
        var restored = OnionServiceBackup.Import(archive);
        Check(restored.Count == 2 && restored[0].Name == "Web" && restored[1].TargetPort == 9443, "Onion service backup roundtrip failed.");
        Console.WriteLine("PASS Onion service backup roundtrip");

        File.WriteAllText(archive, "not a zip");
        ExpectInvalid(() => OnionServiceBackup.Import(archive), "malformed archive");

        using (var file = new FileStream(archive, FileMode.Create, FileAccess.Write))
        using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("../outside.json");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write("{}");
        }
        ExpectInvalid(() => OnionServiceBackup.Import(archive), "path traversal entry");

        ExpectInvalid(() => OnionServiceBackup.Export(archive, new[]
        {
            new OnionServiceEntry { Name = "A!", VirtualPort = 80, TargetPort = 8080 },
            new OnionServiceEntry { Name = "A?", VirtualPort = 81, TargetPort = 8081 }
        }), "colliding service names");
        Console.WriteLine("PASS Onion service backup validation");
    }

    private static void ExpectInvalid(Action action, string label)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException($"Expected {label} to be rejected.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
