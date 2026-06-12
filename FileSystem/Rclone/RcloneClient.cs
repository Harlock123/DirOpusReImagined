using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

    // ---- Async job / stats API (used to drive transfer progress) -------------------------
    //
    // rclone's rc server can run an operation asynchronously: pass "_async": true and it
    // returns a jobid immediately instead of blocking until completion. Passing "_group"
    // tags the job's stats so they can be queried in isolation via core/stats. The transfer
    // layer then polls job/status (done yet?) and core/stats (how far?) until finished.

    /// <summary>
    /// Starts <paramref name="endpoint"/> as a background job tagged with <paramref name="group"/>
    /// and returns its job id. Mutates <paramref name="body"/> to add the _async/_group keys.
    /// </summary>
    public async Task<long> StartAsyncJobAsync(string endpoint, Dictionary<string, object> body,
                                               string group, CancellationToken ct = default)
    {
        body["_async"] = true;
        body["_group"] = group;
        using var doc = await PostAsync(endpoint, body, ct).ConfigureAwait(false);
        return doc.RootElement.TryGetProperty("jobid", out var j) && j.ValueKind == JsonValueKind.Number
            ? j.GetInt64()
            : throw new InvalidOperationException($"rclone {endpoint} did not return a jobid");
    }

    public async Task<RcloneJobStatus> GetJobStatusAsync(long jobid, CancellationToken ct = default)
    {
        using var doc = await PostAsync("job/status", new Dictionary<string, object> { ["jobid"] = jobid }, ct)
            .ConfigureAwait(false);
        var root = doc.RootElement;
        var finished = root.TryGetProperty("finished", out var f) && f.ValueKind == JsonValueKind.True;
        var success  = root.TryGetProperty("success",  out var s) && s.ValueKind == JsonValueKind.True;
        var error    = root.TryGetProperty("error",    out var e) ? e.GetString() ?? "" : "";
        return new RcloneJobStatus(finished, success, error);
    }

    public async Task<RcloneStats> GetStatsAsync(string group, CancellationToken ct = default)
    {
        using var doc = await PostAsync("core/stats", new Dictionary<string, object> { ["group"] = group }, ct)
            .ConfigureAwait(false);
        var root = doc.RootElement;

        string? curName = null;
        long curBytes = 0, curSize = 0;
        if (root.TryGetProperty("transferring", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var t in arr.EnumerateArray())
            {
                curName  = t.TryGetProperty("name", out var n) ? n.GetString() : null;
                curBytes = GetLong(t, "bytes");
                curSize  = GetLong(t, "size");
                break; // report the first in-flight file as "current"
            }
        }

        return new RcloneStats(
            GetLong(root, "bytes"),
            GetLong(root, "totalBytes"),
            GetDouble(root, "speed"),
            curName,
            curBytes,
            curSize);
    }

    public async Task StopJobAsync(long jobid, CancellationToken ct = default)
    {
        using var _ = await PostAsync("job/stop", new Dictionary<string, object> { ["jobid"] = jobid }, ct)
            .ConfigureAwait(false);
    }

    private static long GetLong(JsonElement el, string name)
        => el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt64() : 0L;

    private static double GetDouble(JsonElement el, string name)
        => el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDouble() : 0d;

    public void Dispose() => _http.Dispose();
}

/// <summary>Result of polling job/status for an async rclone job.</summary>
public readonly record struct RcloneJobStatus(bool Finished, bool Success, string Error);

/// <summary>A snapshot from core/stats for a single transfer group.</summary>
public readonly record struct RcloneStats(
    long Bytes,
    long TotalBytes,
    double Speed,
    string? CurrentFile,
    long CurrentFileBytes,
    long CurrentFileSize);
