using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;

namespace SidebarSorter;

internal sealed partial class DesktopApp : Application
{
    public static DesktopApp? Instance { get; private set; }
    public SemaphoreSlim OperationGate { get; } = new(1, 1);
    public ToolSettingsStore SettingsStore { get; } = new();
    public ToolSettings Settings { get; private set; } = new();
    public string Status { get; private set; } = "";
    private AutomaticSorter? automatic;
    private DesktopWindow? window;
    private TrayIcon? tray;
    private bool exiting;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        Instance = this;
        Settings = SettingsStore.Load();
        Localization.SetLanguage(Settings.Language);
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            automatic = new AutomaticSorter(NativeBridge.DataRoot, async () =>
            {
                if (!Settings.AutomaticNameSorting) return;
                await OperationGate.WaitAsync();
                try { await AutomaticSorter.SynchronizeByNameAsync(new SorterService()); }
                finally { OperationGate.Release(); }
            });
            automatic.Completed += error => Dispatcher.UIThread.Post(async () =>
            {
                Status = error?.Message ?? Localization.Text(Settings.AutomaticNameSorting ? "自动保持名称升序" : "自动排序已暂停");
                window?.SetStatus(Status, error != null);
                if (error == null && Settings.AutomaticNameSorting && window != null) await window.RefreshAsync();
            });
            BuildTray();
            if (!DesktopProgram.Arguments.Contains("--background")) ShowWindow();
        }
        base.OnFrameworkInitializationCompleted();
    }

    public void ShowWindow()
    {
        if (exiting) return;
        if (window == null)
        {
            window = new DesktopWindow(this);
            window.Closed += (_, _) => { window = null; GC.Collect(2, GCCollectionMode.Optimized, false); };
        }
        window.Show();
        window.Activate();
    }

    public void SaveSettings(ToolSettings settings)
    {
        SettingsStore.Save(settings);
        Settings = settings;
        Localization.SetLanguage(settings.Language);
        BuildTray();
        automatic?.Request();
    }

    private void BuildTray()
    {
        tray?.Dispose();
        var menu = new NativeMenu();
        var open = new NativeMenuItem(Localization.Text("打开聊天排序"));
        open.Click += (_, _) => ShowWindow();
        var exit = new NativeMenuItem(Localization.Text("退出程序"));
        exit.Click += async (_, _) => await ExitAsync();
        menu.Items.Add(open);
        menu.Items.Add(exit);
        using Stream icon = AssetLoader.Open(new Uri("avares://SidebarSorter/Assets/app.ico"));
        tray = new TrayIcon { Icon = new WindowIcon(icon), ToolTipText = Localization.Text("聊天排序工具"), Menu = menu, IsVisible = true };
        tray.Clicked += (_, _) => ShowWindow();
    }

    public async Task ExitAsync()
    {
        if (exiting) return;
        exiting = true;
        if (automatic != null) await automatic.DisposeAsync();
        await OperationGate.WaitAsync();
        OperationGate.Release();
        tray?.Dispose();
        (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }
}
