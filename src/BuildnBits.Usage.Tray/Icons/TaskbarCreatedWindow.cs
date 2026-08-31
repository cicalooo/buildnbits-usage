using System.Runtime.InteropServices;

namespace BuildnBits.Usage.Tray.Icons;

/// <summary>
/// Listens for the documented TaskbarCreated broadcast so NotifyIcon can be
/// re-registered after Explorer restarts. No injection or reparenting.
/// </summary>
public sealed class TaskbarCreatedWindow : NativeWindow, IDisposable
{
    private readonly uint _taskbarCreated;
    private readonly Action _recreate;

    public TaskbarCreatedWindow(Action recreate)
    {
        _recreate = recreate;
        _taskbarCreated = NativeMethods.RegisterWindowMessage("TaskbarCreated");
        var cp = new CreateParams
        {
            Caption = "BuildnBits.Usage.TaskbarCreated",
            Style = 0,
            ExStyle = 0x80 // WS_EX_TOOLWINDOW
        };
        CreateHandle(cp);
        NativeMethods.ChangeWindowMessageFilterEx(Handle, _taskbarCreated, 1, IntPtr.Zero);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == _taskbarCreated)
        {
            _recreate();
        }

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (Handle != IntPtr.Zero)
        {
            DestroyHandle();
        }
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool ChangeWindowMessageFilterEx(IntPtr hwnd, uint message, uint action, IntPtr pChangeFilterStruct);
    }
}
