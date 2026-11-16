namespace SidebarSorter;

internal sealed partial class MainForm
{
    private const int OpenFromBackgroundMessage = 0x8000 + 121;
    private const int ExitFromBackgroundMessage = 0x8000 + 122;

    protected override void OnFormClosing(FormClosingEventArgs eventArgs)
    {
        if (returnToBackground && busy && eventArgs.CloseReason == CloseReason.UserClosing)
        {
            closeWhenIdle = true;
            eventArgs.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(eventArgs);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == OpenFromBackgroundMessage)
        {
            closeWhenIdle = false;
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Show();
            return;
        }
        if (message.Msg == ExitFromBackgroundMessage)
        {
            closeWhenIdle = true;
            Close();
            return;
        }
        base.WndProc(ref message);
    }
}
