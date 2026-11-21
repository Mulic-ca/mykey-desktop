using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MyKey.Desktop;

public sealed class ModelDetectionException(string message) : Exception(message);

public static class ModelDetectionService
{
    public static async Task<List<string>> DetectAsync(string baseUrl, string apiKey, CancellationToken cancellationToken = default, HttpClient? client = null)
    {
        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) || !string.IsNullOrEmpty(uri.UserInfo))
            throw new ModelDetectionException("Base URL 格式不正确，请填写以 http:// 或 https:// 开头的完整地址。");
        if (string.IsNullOrWhiteSpace(apiKey)) throw new ModelDetectionException("请先填写 API Key。");

        using var ownedClient = client is null ? new HttpClient { Timeout = TimeSpan.FromSeconds(15) } : null;
        client ??= ownedClient!;
        HttpStatusCode? lastStatus = null;
        foreach (var url in BuildModelUrlCandidates(baseUrl))
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.ValueKind != JsonValueKind.Object ||
                    !document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                    throw new ModelDetectionException("平台返回的内容不是有效的模型列表，请检查 Base URL 和平台接口是否支持模型检测。");
                var models = data.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.Object)
                    .Select(item => item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() ?? "" : "")
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (data.GetArrayLength() > 0 && models.Count == 0)
                    throw new ModelDetectionException("平台返回的模型信息缺少有效的模型名称，请检查平台接口。");
                return models;
            }
            lastStatus = response.StatusCode;
            if (response.StatusCode is not HttpStatusCode.NotFound and not HttpStatusCode.MethodNotAllowed)
                throw new ModelDetectionException(DescribeStatus(response.StatusCode, body));
        }
        throw new ModelDetectionException(DescribeStatus(lastStatus ?? HttpStatusCode.NotFound, ""));
    }

    public static string DescribeError(Exception error) => error switch
    {
        ModelDetectionException => error.Message,
        OperationCanceledException => "请求超时或已取消，请检查网络后重试。",
        HttpRequestException { HttpRequestError: HttpRequestError.NameResolutionError } => "无法解析平台域名，请检查 Base URL、网络或 DNS 设置。",
        HttpRequestException { HttpRequestError: HttpRequestError.SecureConnectionError } => "无法建立安全连接，请检查系统时间、代理或平台证书。",
        HttpRequestException => "无法连接到 API 平台，请检查网络、代理和 Base URL 后重试。",
        JsonException => "平台返回的内容无法解析，请检查 Base URL 是否指向 API 接口。",
        UriFormatException or FormatException or ArgumentException => "请求配置格式不正确，请检查 Base URL 和 API Key。",
        IOException => "读取平台响应失败，请检查网络后重试。",
        _ => "模型检测未完成，请检查平台配置并稍后重试。"
    };

    private static string DescribeStatus(HttpStatusCode status, string body)
    {
        // Never display a provider's raw body: it may be English or echo credentials.
        var message = (int)status switch
        {
            400 => "平台无法接受此请求，请检查 Base URL 和接口配置。",
            401 => "API Key 无效或已失效，请检查默认 Key 是否填写正确。",
            402 => "平台提示余额或额度不足，请前往平台查看。",
            403 => "当前 Key 没有访问权限，或平台限制了当前网络的访问。",
            404 or 405 => "未找到可用的模型列表接口，请检查 Base URL，或手动添加模型。",
            408 or 504 => "平台响应超时，请稍后重试。",
            429 when body.Contains("quota", StringComparison.OrdinalIgnoreCase) || body.Contains("balance", StringComparison.OrdinalIgnoreCase) || body.Contains("credit", StringComparison.OrdinalIgnoreCase) => "平台提示额度或余额不足，请前往平台查看。",
            429 => "请求过于频繁或已达到平台限额，请稍后重试。",
            >= 500 => "API 平台暂时出现服务异常，请稍后重试。",
            _ => "平台未能完成模型检测，请检查接口配置或稍后重试。"
        };
        return $"{message}（状态码：{(int)status}）";
    }

    private static IEnumerable<string> BuildModelUrlCandidates(string baseUrl)
    {
        var trimmed = baseUrl.Trim().TrimEnd('/');
        if (Regex.IsMatch(trimmed, @"/v\d+$"))
        {
            yield return trimmed + "/models";
            if (!trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) yield return trimmed + "/v1/models";
        }
        else yield return trimmed + "/v1/models";
        var suffixes = new[] { "/api/claudecode", "/api/anthropic", "/apps/anthropic", "/api/coding", "/claudecode", "/anthropic", "/step_plan", "/coding", "/claude" };
        foreach (var suffix in suffixes)
        {
            if (!trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
            var root = trimmed[..^suffix.Length].TrimEnd('/');
            if (!string.IsNullOrWhiteSpace(root) && root.Contains("://", StringComparison.Ordinal))
            {
                yield return root + "/v1/models";
                yield return root + "/models";
            }
            break;
        }
    }
}
