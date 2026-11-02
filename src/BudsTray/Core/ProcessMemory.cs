using System;
using System.Runtime.InteropServices;

namespace BudsTray.Core;

public static class ProcessMemory
{
    /// <summary>Hands startup and UI memory back to the OS while the app only waits for the buds.</summary>
    public static void Trim()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        SetProcessWorkingSetSize(GetCurrentProcess(), -1, -1);
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessWorkingSetSize(IntPtr process, nint minimumSize, nint maximumSize);
}
