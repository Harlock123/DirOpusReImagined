using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DirOpusReImagined.FileSystem.Rclone;

public sealed class RcloneClient : IDisposable
{
    private const int RequestLogCapacity = 100;

    private static readonly ConcurrentQueue<string> _requestLog = new();

    public static string[] RecentRequests => _requestLog.ToArray();

    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public RcloneClient(string baseUrl, string user, string password)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        var creds = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", creds);
    }

    public async Task<JsonDocument> PostAsync(string endpoint, object? body = null, CancellationToken ct = default)
    {
        var json = body is null ? "{}" : JsonSerializer.Serialize(body);
        var logLine = $"POST {endpoint}  body={json}";
        AppendRequestLog(logLine);

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        // rclone's rc server does an exact-string match on Content-Type — "application/json; charset=utf-8"
        // skips its JSON decoder and the body is silently ignored. Strip charset to match exactly.
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        var url = $"{_baseUrl}/{endpoint.TrimStart('/')}";

        using var resp = await _http.PostAsync(url, content, ct).ConfigureAwait(false);
        var respBody = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
        {
            AppendRequestLog($"  -> {(int)resp.StatusCode} {resp.StatusCode}: {respBody}");
            throw new HttpRequestException($"{(int)resp.StatusCode} {resp.StatusCode}: {respBody}");
        }

        return JsonDocument.Parse(respBody);
    }

    private static void AppendRequestLog(string line)
    {
        _requestLog.Enqueue($"{DateTime.Now:HH:mm:ss} {line}");
        while (_requestLog.Count > RequestLogCapacity && _requestLog.TryDequeue(out _)) { }
    }

    public Task<JsonDocument> NoopAsync(CancellationToken ct = default) => PostAsync("rc/noop", null, ct);

    public async Task<bool> WaitForReadyAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var _ = await NoopAsync(ct).ConfigureAwait(false);
                return true;
            }
            catch
            {
                try { await Task.Delay(100, ct).ConfigureAwait(false); } catch { return false; }
            }
        }
        return false;
    }

    public void Dispose() => _http.Dispose();
}
