using Microsoft.Win32;
using System.IO;
using System.Runtime.Versioning;
using System.Text.Json;

namespace OrbotPc.Core;

public interface IWindowsRunKeyStore
{
    RunKeyValue? Read(string valueName);
    void Write(string valueName, RunKeyValue? value);
}

public sealed record RunKeyValue(
    string Kind,
    string? StringValue = null,
    string[]? MultiStringValue = null,
    byte[]? BinaryValue = null,
    long? NumericValue = null);

/// <summary>Owns one current-user Run value and restores the exact prior value when disabled.</summary>
public sealed class WindowsStartupController
{
    public const string RunValueName = "OrbotPC";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly IWindowsRunKeyStore _store;
    private readonly string _backupPath;
    private readonly string _ownedCommand;

    public WindowsStartupController(string stateDirectory, string executablePath, IWindowsRunKeyStore? store = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        _store = store ?? new CurrentUserRunKeyStore();
        Directory.CreateDirectory(stateDirectory);
        _backupPath = Path.Combine(Path.GetFullPath(stateDirectory), "windows-startup-backup.json");
        _ownedCommand = QuoteCommandArgument(Path.GetFullPath(executablePath));
    }

    public bool IsEnabled
    {
        get
        {
            var backup = ReadBackup();
            var current = _store.Read(RunValueName);
            return backup is not null && IsOwnedValue(current, backup.OwnedCommand);
        }
    }

    public void Enable()
    {
        var current = _store.Read(RunValueName);
        var previous = ReadBackup();
        if (previous is not null && IsOwnedValue(current, previous.OwnedCommand) &&
            string.Equals(previous.OwnedCommand, _ownedCommand, StringComparison.Ordinal))
            return;

        var original = previous is not null && IsOwnedValue(current, previous.OwnedCommand)
            ? previous.Original
            : current;
        var backup = new StartupBackup(original, _ownedCommand);
        WriteBackup(backup);
        try
        {
            _store.Write(RunValueName, new RunKeyValue(RegistryValueKind.String.ToString(), _ownedCommand));
        }
        catch
        {
            TryRestoreAfterFailedEnable(backup);
            TryDeleteBackup();
            throw;
        }
    }

    /// <returns>True if this controller removed its own startup command.</returns>
    public bool Disable()
    {
        var backup = ReadBackup();
        if (backup is null)
        {
            TryDeleteBackup();
            return false;
        }

        var current = _store.Read(RunValueName);
        var ownsCurrent = IsOwnedValue(current, backup.OwnedCommand);
        if (ownsCurrent) _store.Write(RunValueName, backup.Original);
        TryDeleteBackup();
        return ownsCurrent;
    }

    private static bool IsOwnedValue(RunKeyValue? value, string command) =>
        value is { Kind: nameof(RegistryValueKind.String) } &&
        string.Equals(value.StringValue, command, StringComparison.Ordinal);

    private StartupBackup? ReadBackup()
    {
        if (!File.Exists(_backupPath)) return null;
        try
        {
            var backup = JsonSerializer.Deserialize<StartupBackup>(File.ReadAllText(_backupPath));
            if (backup is null || string.IsNullOrWhiteSpace(backup.OwnedCommand))
                throw new InvalidDataException("Windows startup backup is invalid.");
            ValidateValue(backup.Original);
            return backup;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Windows startup backup is invalid.", ex);
        }
    }

    private void WriteBackup(StartupBackup backup)
    {
        var temporaryPath = _backupPath + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(backup));
            File.Move(temporaryPath, _backupPath, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch { }
        }
    }

    private void TryRestoreAfterFailedEnable(StartupBackup backup)
    {
        try
        {
            if (IsOwnedValue(_store.Read(RunValueName), backup.OwnedCommand))
                _store.Write(RunValueName, backup.Original);
        }
        catch { }
    }

    private void TryDeleteBackup()
    {
        try { File.Delete(_backupPath); } catch { }
    }

    private static void ValidateValue(RunKeyValue? value)
    {
        if (value is null) return;
        if (!Enum.TryParse<RegistryValueKind>(value.Kind, ignoreCase: false, out var kind) || kind == RegistryValueKind.None)
            throw new InvalidDataException("Windows startup backup contains an unsupported registry value type.");
        var valid = kind switch
        {
            RegistryValueKind.String or RegistryValueKind.ExpandString => value.StringValue is not null,
            RegistryValueKind.MultiString => value.MultiStringValue is not null,
            RegistryValueKind.Binary => value.BinaryValue is not null,
            RegistryValueKind.DWord or RegistryValueKind.QWord => value.NumericValue is not null,
            _ => false
        };
        if (!valid) throw new InvalidDataException("Windows startup backup value data does not match its type.");
    }

    private static string QuoteCommandArgument(string value)
    {
        if (value.Length > 0 && !value.Any(char.IsWhiteSpace) && !value.Contains('"')) return value;
        var result = new System.Text.StringBuilder("\"");
        var backslashes = 0;
        foreach (var character in value)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }
            if (character == '"')
            {
                result.Append('\\', backslashes * 2 + 1).Append('"');
                backslashes = 0;
                continue;
            }
            result.Append('\\', backslashes).Append(character);
            backslashes = 0;
        }
        result.Append('\\', backslashes * 2).Append('"');
        return result.ToString();
    }

    private sealed record StartupBackup(RunKeyValue? Original, string OwnedCommand);

    [SupportedOSPlatform("windows")]
    private sealed class CurrentUserRunKeyStore : IWindowsRunKeyStore
    {
        public RunKeyValue? Read(string valueName)
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            if (key is null || key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not { } value)
                return null;
            var kind = key.GetValueKind(valueName);
            return kind switch
            {
                RegistryValueKind.String => new RunKeyValue(kind.ToString(), Convert.ToString(value)),
                RegistryValueKind.ExpandString => new RunKeyValue(kind.ToString(), Convert.ToString(value)),
                RegistryValueKind.MultiString => new RunKeyValue(kind.ToString(), MultiStringValue: (string[])value),
                RegistryValueKind.Binary => new RunKeyValue(kind.ToString(), BinaryValue: (byte[])value),
                RegistryValueKind.DWord => new RunKeyValue(kind.ToString(), NumericValue: Convert.ToInt32(value)),
                RegistryValueKind.QWord => new RunKeyValue(kind.ToString(), NumericValue: Convert.ToInt64(value)),
                _ => throw new InvalidDataException($"Unsupported current-user Run value type: {kind}.")
            };
        }

        public void Write(string valueName, RunKeyValue? value)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                ?? throw new InvalidOperationException("Could not open the current user's Run key.");
            if (value is null)
            {
                key.DeleteValue(valueName, throwOnMissingValue: false);
                return;
            }
            ValidateValue(value);
            var kind = Enum.Parse<RegistryValueKind>(value.Kind, ignoreCase: false);
            object data = kind switch
            {
                RegistryValueKind.String or RegistryValueKind.ExpandString => value.StringValue!,
                RegistryValueKind.MultiString => value.MultiStringValue!,
                RegistryValueKind.Binary => value.BinaryValue!,
                RegistryValueKind.DWord => checked((int)value.NumericValue!.Value),
                RegistryValueKind.QWord => value.NumericValue!.Value,
                _ => throw new InvalidDataException($"Unsupported current-user Run value type: {kind}.")
            };
            key.SetValue(valueName, data, kind);
        }
    }
}
