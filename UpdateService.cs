using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace MyKey.Desktop;

public sealed record AppRelease(Version Version, string Notes, Uri Download, string Sha256, long Size);

public static class UpdateService
{
    public const string Repository = "Mulic-ca/mykey-desktop";
    public static Version CurrentVersion => typeof(UpdateService).Assembly.GetName().Version ?? new(1, 1, 0);
    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MYKEY/" + CurrentVersion.ToString(3));
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    public static async Task<AppRelease?> CheckAsync(HttpClient? client = null, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        using var response = await (client ?? Client).GetAsync($"https://api.github.com/repos/{Repository}/releases/latest", timeout.Token);
        if (response.StatusCode == HttpStatusCode.NotFound) throw new InvalidOperationException("尚无可用的正式发布版本");
        response.EnsureSuccessStatusCode();
        return ParseRelease(await response.Content.ReadAsStringAsync(timeout.Token), CurrentVersion);
    }

    public static AppRelease? ParseRelease(string json, Version current)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
        if (!Version.TryParse(root.GetProperty("tag_name").GetString()?.TrimStart('v', 'V'), out var version))
            throw new InvalidDataException("更新版本号无效");
        if (new Version(version.Major, version.Minor, Math.Max(0, version.Build)) <= new Version(current.Major, current.Minor, Math.Max(0, current.Build))) return null;
        var name = $"MYKEY-Setup-{version.ToString(3)}-x64.exe";
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != name) continue;
            var uri = new Uri(asset.GetProperty("browser_download_url").GetString()!);
            if (uri.Scheme != "https" || uri.Host != "github.com" || !uri.AbsolutePath.StartsWith($"/{Repository}/releases/download/", StringComparison.Ordinal))
                throw new InvalidDataException("更新包地址无效");
            var digest = asset.TryGetProperty("digest", out var value) ? value.GetString() : null;
            if (digest is null || !digest.StartsWith("sha256:") || digest.Length != 71 || !digest[7..].All(Uri.IsHexDigit))
                throw new InvalidDataException("更新包缺少 SHA-256 校验信息");
            var size = asset.GetProperty("size").GetInt64();
            if (size <= 0 || size > 500L * 1024 * 1024) throw new InvalidDataException("更新包大小无效");
            return new(version, root.TryGetProperty("body", out var notes) ? notes.GetString() ?? "" : "", uri, digest[7..], size);
        }
        throw new InvalidDataException("发布版本缺少 Windows x64 安装包");
    }

    public static async Task<string> DownloadAsync(AppRelease release, IProgress<double>? progress, CancellationToken token, HttpClient? client = null, string? directory = null)
    {
        directory ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MYKEY", "updates", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"MYKEY-Setup-{release.Version.ToString(3)}-x64.exe");
        var temporary = path + ".partial";
        try
        {
            using var response = await (client ?? Client).GetAsync(release.Download, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(token))
            await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long total = 0;
                int count;
                while ((count = await input.ReadAsync(buffer, token)) > 0)
                {
                    total += count;
                    if (total > release.Size) throw new InvalidDataException("更新包大小不符");
                    await output.WriteAsync(buffer.AsMemory(0, count), token);
                    progress?.Report(100.0 * total / release.Size);
                }
                if (total != release.Size) throw new InvalidDataException("更新包未下载完整");
            }
            await using (var file = File.OpenRead(temporary))
            {
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(file, token));
                if (!hash.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("更新包校验失败，请重新下载");
            }
            File.Move(temporary, path, true);
            return path;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static void StartInstaller(string installer)
    {
        var directory = Path.GetDirectoryName(installer)!;
        var script = Path.Combine(directory, "install-update.ps1");
        using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("MyKey.Desktop.install-update.ps1")!)
        using (var output = File.Create(script)) resource.CopyTo(output);
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-ParentId", Environment.ProcessId.ToString(), "-Installer", installer, "-InstallDirectory", AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar) })
            start.ArgumentList.Add(argument);
        _ = Process.Start(start) ?? throw new InvalidOperationException("无法启动更新程序");
    }
}
