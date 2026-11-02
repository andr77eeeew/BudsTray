using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace BudsTray.UI;

public static class TrayCorner
{
    /// <summary>Puts a shown window in the screen corner next to the tray, whichever edge the taskbar is on.</summary>
    public static void Place(Window window, Forms.Screen screen)
    {
        if (!window.IsVisible)
            return;

        System.Drawing.Rectangle area = screen.WorkingArea;
        System.Drawing.Rectangle bounds = screen.Bounds;

        DpiScale dpi = VisualTreeHelper.GetDpi(window);
        int width = (int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX);
        int height = (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY);

        int x = area.Left > bounds.Left ? area.Left : area.Right - width;
        int y = area.Top > bounds.Top ? area.Top : area.Bottom - height;

        IntPtr handle = new WindowInteropHelper(window).Handle;
        SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
    }

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
