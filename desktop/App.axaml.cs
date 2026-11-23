using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace SidebarSorter;

internal sealed partial class DesktopApp : Application
{
    public static DesktopApp? Instance { get; private set; }
    internal static TaskCompletionSource<DesktopApp> Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public SemaphoreSlim OperationGate { get; } = new(1, 1);
    public ToolSettingsStore SettingsStore { get; } = new();
    public ToolSettings Settings { get; private set; } = new();
    public string Status { get; private set; } = "";
    private AutomaticSorter? observer;
    private DesktopWindow? window;
    private bool exiting;
    internal bool IsClosing => exiting;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        Instance = this;
        Settings = SettingsStore.Load();
        if (Settings.AutomaticNameSorting) Settings = Settings with { ProjectSortMode = 0, ChatSortMode = 0 };
        Localization.SetLanguage(Settings.Language);
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            observer = new AutomaticSorter(NativeBridge.DataRoot, async () =>
            {
                BackgroundStatus status = await BackgroundClient.ReadStatusAsync();
                Dispatcher.UIThread.Post(async () =>
                {
                    if (exiting) return;
                    Status = status.Message;
                    window?.SetStatus(Status, status.Error);
                    if (Settings.AutomaticNameSorting && window?.IsVisible == true) await window.RefreshAsync(refreshChatGptHistory: false);
                });
            });
            observer.Completed += error =>
            {
                if (error != null) Dispatcher.UIThread.Post(() => window?.SetStatus(error.Message, true));
            };
            ShowWindow();
            Ready.TrySetResult(this);
        }
        base.OnFrameworkInitializationCompleted();
    }

    public void ShowWindow()
    {
        if (exiting) return;
        if (window == null)
        {
            var created = new DesktopWindow(this);
            window = created;
            created.Closing += (_, args) =>
            {
                if (exiting) return;
                args.Cancel = true;
                created.Hide();
                Dispatcher.UIThread.Post(async () =>
                {
                    await OperationGate.WaitAsync();
                    OperationGate.Release();
                    if (!created.IsVisible) await ExitAsync();
                });
            };
        }
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Show();
        window.Activate();
    }

    public void SaveSettings(ToolSettings settings)
    {
        SettingsStore.Save(settings);
        Settings = settings;
        Localization.SetLanguage(settings.Language);
        _ = NotifySettingsAsync();
    }

    private async Task NotifySettingsAsync()
    {
        try { await BackgroundClient.NotifyAsync("settings"); }
        catch (Exception error) when (error is IOException or TimeoutException) { window?.SetStatus(error.Message, true); }
    }

    public async Task ExitAsync()
    {
        if (exiting) return;
        exiting = true;
        if (observer != null) await observer.DisposeAsync();
        await OperationGate.WaitAsync();
        OperationGate.Release();
        try { await BackgroundClient.NotifyAsync("compact"); }
        catch (Exception error) when (error is IOException or TimeoutException) { Status = error.Message; }
        (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }
}
