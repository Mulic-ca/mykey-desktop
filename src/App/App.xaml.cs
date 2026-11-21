using System.Windows;
using System.Windows.Threading;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace MyKey.Desktop;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private Mutex? _instance;
    private EventWaitHandle? _activate;
    private RegisteredWaitHandle? _activationWait;
    private bool _ownsInstance;
    private bool _exiting;
    private TrayIcon? _tray;
    private DispatcherTimer? _trayMaintenance;
    private Task? _trayMaintenanceWork;
    private bool _openingWindow;
    private Task<AppRelease?>? _trayUpdateCheck;
    private CancellationTokenSource? _trayUpdateCancellation;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (AppPaths.IsTestMode && System.Reflection.Assembly.GetEntryAssembly() != typeof(App).Assembly)
        {
            base.OnStartup(e);
            return;
        }
        var identity = WindowsIdentity.GetCurrent().User!.Value;
        if (AppPaths.IsTestMode)
            identity += Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(AppPaths.DataDirectory.ToUpperInvariant())))[..16];
        var name = @"Local\MYKEY-" + identity;
        _instance = new Mutex(false, name);
        _activate = new EventWaitHandle(false, EventResetMode.AutoReset, name + "-activate");
        try { _ownsInstance = _instance.WaitOne(0); }
        catch (AbandonedMutexException) { _ownsInstance = true; }
        if (!_ownsInstance)
        {
            _activate.Set();
            Shutdown();
            return;
        }
        base.OnStartup(e);
        _tray = new TrayIcon(ShowMainWindow, ExitFromTray);
        _activationWait = ThreadPool.RegisterWaitForSingleObject(_activate, (_, _) =>
            Dispatcher.BeginInvoke(ShowMainWindow), null, Timeout.Infinite, false);
        if (e.Args.Any(arg => arg.Equals("--tray", StringComparison.OrdinalIgnoreCase)
                           || arg.Equals("--silent", StringComparison.OrdinalIgnoreCase)))
        {
            // Keep the dispatcher and tray alive without building a WPF window or reading the DB at sign-in.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _trayMaintenance = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            _trayMaintenance.Tick += RunTrayMaintenance;
            _trayMaintenance.Start();
            if (!AppPaths.IsTestMode && new AppSettingsStore().Load().CheckUpdatesOnStartup)
            {
                _trayUpdateCancellation = new CancellationTokenSource();
                _trayUpdateCheck = UpdateService.CheckAsync(cancellationToken: _trayUpdateCancellation.Token);
                // The window may never open. Observe failures even when there is no status UI.
                _ = _trayUpdateCheck.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        else ShowMainWindow();
    }

    private async void ShowMainWindow()
    {
        if (_exiting || _openingWindow) return;
        if (MainWindow is MainWindow open)
        {
            open.ShowWindowFromTray();
            return;
        }
        _openingWindow = true;
        try
        {
            StopTrayMaintenance();
            // Do not race the cleanup writer against foreground startup, and keep the tray responsive while waiting.
            if (_trayMaintenanceWork is { } pending) await pending;
            if (_exiting) return;
            _trayMaintenanceWork = null;
            var settings = new AppSettingsStore().Load();
            ThemeManager.Apply(settings.Theme);
            var window = new MainWindow(settings, _trayUpdateCheck);
            _trayUpdateCheck = null;
            MainWindow = window;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            window.ShowWindowFromTray();
        }
        catch (Exception ex) when (ex is System.IO.InvalidDataException or System.IO.IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException or TypeInitializationException)
        {
            var detail = ex is System.IO.InvalidDataException ? ex.Message : "请检查数据库文件、目录权限和加密组件，再重试。";
            MessageBox.Show($"无法打开 MYKEY 数据库。{detail}", "MYKEY", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
        finally { _openingWindow = false; }
    }

    private void RunTrayMaintenance(object? sender, EventArgs e)
    {
        if (_trayMaintenanceWork is { IsCompleted: false }) return;
        _trayMaintenanceWork = Task.Run(() =>
        {
            try { MyKeyRepository.PurgeExistingDatabaseFromTray(); }
            catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("MYKEY tray maintenance failed: {0}", ex.GetType().Name); }
        });
    }

    private void StopTrayMaintenance()
    {
        if (_trayMaintenance is null) return;
        _trayMaintenance.Stop();
        _trayMaintenance.Tick -= RunTrayMaintenance;
        _trayMaintenance = null;
    }

    private void ExitFromTray()
    {
        _exiting = true;
        if (MainWindow is MainWindow open) open.ExitApplication();
        else Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _exiting = true;
        StopTrayMaintenance();
        _tray?.Dispose();
        _trayUpdateCancellation?.Cancel();
        _trayUpdateCancellation?.Dispose();
        _activationWait?.Unregister(null);
        _activate?.Dispose();
        if (_ownsInstance) _instance?.ReleaseMutex();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
