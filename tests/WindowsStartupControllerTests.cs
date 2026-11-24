using Microsoft.Win32;
using OrbotPc.Core;

namespace OrbotPc.Tests;

public static class WindowsStartupControllerTests
{
    public static void Run(string root)
    {
        Directory.CreateDirectory(root);
        var store = new MemoryRunKeyStore();
        var state = Path.Combine(root, "restore-existing");
        var prior = new RunKeyValue(RegistryValueKind.ExpandString.ToString(), "%ProgramFiles%\\Legacy\\legacy.exe");
        store.Write(WindowsStartupController.RunValueName, prior);
        store.Write("OtherApp", new RunKeyValue(RegistryValueKind.String.ToString(), "other.exe"));
        var controller = new WindowsStartupController(state, Path.Combine(root, "Orbot PC.exe"), store);

        controller.Enable();
        Check(controller.IsEnabled, "Enable did not install the owned current-user Run value.");
        Check(store.Read("OtherApp")?.StringValue == "other.exe", "Enable changed an unrelated Run value.");
        Check(controller.Disable(), "Disable did not identify and remove its own value.");
        Check(store.Read(WindowsStartupController.RunValueName) == prior, "Disable did not restore the exact prior value and kind.");
        Check(store.Read("OtherApp")?.StringValue == "other.exe", "Disable changed an unrelated Run value.");
        Console.WriteLine("PASS current-user startup enable/restore preserves prior registry type and other values");

        var externalEditState = Path.Combine(root, "external-edit");
        var externalStore = new MemoryRunKeyStore();
        var externalController = new WindowsStartupController(externalEditState, Path.Combine(root, "OrbotPC.exe"), externalStore);
        externalController.Enable();
        var userReplacement = new RunKeyValue(RegistryValueKind.String.ToString(), "user-selected.exe");
        externalStore.Write(WindowsStartupController.RunValueName, userReplacement);
        Check(!externalController.Disable(), "Disable should report that the current value is no longer owned.");
        Check(externalStore.Read(WindowsStartupController.RunValueName) == userReplacement, "Disable overwrote a value changed by the user.");
        Console.WriteLine("PASS current-user startup disable leaves a subsequently edited value untouched");

        var updateState = Path.Combine(root, "command-update");
        var updateStore = new MemoryRunKeyStore();
        updateStore.Write(WindowsStartupController.RunValueName, prior);
        var first = new WindowsStartupController(updateState, Path.Combine(root, "OrbotPC.exe"), updateStore);
        first.Enable();
        var second = new WindowsStartupController(updateState, Path.Combine(root, "OrbotPC-new.exe"), updateStore);
        second.Enable();
        Check(second.Disable(), "Updated startup command was not recognized as owned.");
        Check(updateStore.Read(WindowsStartupController.RunValueName) == prior, "Changing the executable path lost the original Run value.");
        Console.WriteLine("PASS startup command update retains the original prior Run value");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class MemoryRunKeyStore : IWindowsRunKeyStore
    {
        private readonly Dictionary<string, RunKeyValue> _values = new(StringComparer.OrdinalIgnoreCase);

        public RunKeyValue? Read(string valueName) => _values.TryGetValue(valueName, out var value) ? value : null;

        public void Write(string valueName, RunKeyValue? value)
        {
            if (value is null) _values.Remove(valueName);
            else _values[valueName] = value;
        }
    }
}
