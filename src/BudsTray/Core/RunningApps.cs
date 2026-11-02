using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace BudsTray.Core;

public static class RunningApps
{
    private static readonly HashSet<string> NotApps = new(StringComparer.OrdinalIgnoreCase)
    {
        "ApplicationFrameHost", "explorer", "ShellExperienceHost", "StartMenuExperienceHost", "SystemSettings", "TextInputHost", "BudsTray",
    };

    public static HashSet<string> AllNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Process process in Process.GetProcesses())
        {
            using (process)
                names.Add(process.ProcessName);
        }
        return names;
    }

    /// <summary>Applications with a window, which is what a person would recognise in a list.</summary>
    public static List<string> Windowed()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Process process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.MainWindowHandle != IntPtr.Zero && !NotApps.Contains(process.ProcessName))
                        names.Add(process.ProcessName);
                }
                catch (InvalidOperationException)
                {
                    // It exited while being looked at.
                }
            }
        }
        return names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string NormalizeName(string text)
    {
        string name = text.Trim();
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }
}
