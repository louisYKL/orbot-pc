using OrbotPc.Core;

namespace OrbotPc.Tests;

internal static class SnowflakeVolunteerRuntimeTests
{
    public static async Task RunAsync(string root)
    {
        var arguments = SnowflakeVolunteerRuntime.BuildArguments();
        Check(arguments.SequenceEqual(["-capacity", "1", "-disable-stats-logger", "-summary-interval", "0s"]),
            "Snowflake volunteer mode must use capacity one and disable periodic stats.");
        try
        {
            SnowflakeVolunteerRuntime.BuildArguments(0);
            throw new InvalidOperationException("A zero-capacity volunteer proxy was accepted.");
        }
        catch (ArgumentOutOfRangeException) { }

        var runtime = new SnowflakeVolunteerRuntime(root);
        var version = await runtime.GetVersionAsync();
        Check(version.Contains("snowflake-proxy", StringComparison.OrdinalIgnoreCase), "The bundled Snowflake relay version could not be read.");
        Console.WriteLine("PASS bundled Snowflake volunteer binary, version probe, and bounded startup arguments");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
