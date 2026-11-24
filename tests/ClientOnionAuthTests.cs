using OrbotPc.Core;

namespace OrbotPc.Tests;

internal static class ClientOnionAuthTests
{
    public static void Run(string root)
    {
        var path = Path.Combine(root, ".data", "test-client-onion-auth-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ClientOnionAuthStore(path, requirePrivateAcl: false);
            var domain = new string('a', 56) + ".onion";
            var key = new string('b', 51) + "a";
            store.Add(domain, key);
            var authFile = Path.Combine(path, domain + ".auth_private");
            Check(File.ReadAllText(authFile) == $"{domain}:descriptor:x25519:{key}", "The .auth_private file format was not written correctly.");
            Check(store.Read(domain) == $"{domain}:descriptor:x25519:{key}", "The authorization export reader did not return the validated native entry.");
            Check(store.ListDomains().SequenceEqual([domain]), "The authorization list did not return the stored domain.");

            var importedDomain = new string('c', 56) + ".onion";
            var imported = store.Import($"{importedDomain}:descriptor:x25519:{key}\r\n");
            Check(imported == importedDomain && store.ListDomains().Count == 2, "A valid .auth_private entry was not imported.");
            Check(store.Remove(importedDomain) && !store.Remove(importedDomain), "Authorization removal returned an incorrect result.");

            ExpectFailure(() => store.Add("../outside", key), "A path-like onion domain was accepted.");
            ExpectFailure(() => store.Add(domain, "not-a-private-key"), "An invalid private key was accepted.");
            ExpectFailure(() => store.Add(domain, new string('b', 52)), "An invalid base32 padding character was accepted.");
            ExpectFailure(() => store.Import($"{domain}:descriptor:ed25519:{key}"), "An unsupported authorization key type was accepted.");
            Console.WriteLine("PASS v3 onion client authorization storage, import, validation, and removal");
        }
        finally
        {
            try { Directory.Delete(path, recursive: true); } catch (IOException) { }
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void ExpectFailure(Action action, string message)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException(message);
    }
}
