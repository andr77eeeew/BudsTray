using System.Runtime.InteropServices;

namespace BudsTray.UI;

public static class UserPresence
{
    private const int NotPresent = 1;
    private const int Busy = 2;
    private const int RunningFullScreen = 3;
    private const int PresentationMode = 4;

    /// <summary>True during a full-screen app, a presentation or a locked screen, when a pop-up would only get in the way.</summary>
    public static bool IsBusy =>
        SHQueryUserNotificationState(out int state) == 0
        && state is NotPresent or Busy or RunningFullScreen or PresentationMode;

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);
}
