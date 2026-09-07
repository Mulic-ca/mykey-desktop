using System.Configuration;
using System.Data;
using System.Windows;
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
        ThemeManager.Apply(new AppSettingsStore().Load().Theme);
        var window = new MainWindow();
        MainWindow = window;
        _activationWait = ThreadPool.RegisterWaitForSingleObject(_activate, (_, _) =>
            Dispatcher.BeginInvoke(() => window.ShowWindowFromTray()), null, Timeout.Infinite, false);
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _activationWait?.Unregister(null);
        _activate?.Dispose();
        if (_ownsInstance) _instance?.ReleaseMutex();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
