using System;
using Microsoft.Win32;

namespace BudsTray.Core;

public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "BudsTray";
    public const string BackgroundArgument = "--background";

    public static bool IsEnabled
    {
        get
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
        set
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value)
                key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" {BackgroundArgument}");
            else
                key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
