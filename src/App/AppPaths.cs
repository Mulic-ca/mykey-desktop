using System.IO;

namespace MyKey.Desktop;

public static class AppPaths
{
    public static bool IsTestMode => Environment.GetCommandLineArgs().Contains("--test-mode");
    public static string DataDirectory
    {
        get
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, "--data-dir");
            return IsTestMode && index >= 0 && index + 1 < args.Length
                ? Path.GetFullPath(args[index + 1])
                : Path.Combine(AppContext.BaseDirectory, "data");
        }
    }
}
