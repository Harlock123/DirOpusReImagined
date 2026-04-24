using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DirOpusReImagined.FileSystem.Rclone;

public sealed record ProviderExample(string Value, string Help);

public sealed record ProviderOption(
    string Name,
    string Help,
    string Type,
    bool Required,
    bool Sensitive,
    bool Advanced,
    int Hide,
    string DefaultStr,
    List<ProviderExample> Examples
);

public sealed record ProviderInfo(
    string Name,
    string Description,
    List<ProviderOption> Options
);

public static class RcloneRemoteManager
{
    public static async Task<List<string>> ListRemotesAsync()
    {
        var client = await RcloneService.GetClientAsync().ConfigureAwait(false);
        using var doc = await client.PostAsync("config/listremotes").ConfigureAwait(false);

        var remotes = new List<string>();
        if (doc.RootElement.TryGetProperty("remotes", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in arr.EnumerateArray())
                if (r.GetString() is string name) remotes.Add(name);
        }
        return remotes;
    }

    public static async Task<List<ProviderInfo>> GetProvidersAsync()
    {
        var client = await RcloneService.GetClientAsync().ConfigureAwait(false);
        using var doc = await client.PostAsync("config/providers").ConfigureAwait(false);

        var providers = new List<ProviderInfo>();
        if (!doc.RootElement.TryGetProperty("providers", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return providers;

        foreach (var p in arr.EnumerateArray())
        {
            var name = p.TryGetProperty("Name", out var np) ? np.GetString() ?? "" : "";
            var desc = p.TryGetProperty("Description", out var dp) ? dp.GetString() ?? "" : "";
            var options = new List<ProviderOption>();

            if (p.TryGetProperty("Options", out var opts) && opts.ValueKind == JsonValueKind.Array)
            {
                foreach (var o in opts.EnumerateArray())
                {
                    options.Add(new ProviderOption(
                        Name:       GetStr(o, "Name"),
                        Help:       GetStr(o, "Help"),
                        Type:       GetStr(o, "Type"),
                        Required:   GetBool(o, "Required"),
                        Sensitive:  GetBool(o, "Sensitive"),
                        Advanced:   GetBool(o, "Advanced"),
                        Hide:       GetInt(o, "Hide"),
                        DefaultStr: GetStr(o, "DefaultStr"),
                        Examples:   GetExamples(o)
                    ));
                }
            }

            providers.Add(new ProviderInfo(name, desc, options));
        }

        providers.Sort((a, b) => string.Compare(a.Description, b.Description, StringComparison.OrdinalIgnoreCase));
        return providers;
    }

    /// <summary>
    /// Returns true if the provider uses OAuth — detected by a hidden "token" option.
    /// </summary>
    public static bool IsOAuthProvider(ProviderInfo p)
        => p.Options.Any(o => o.Name == "token" && (o.Hide & 1) != 0);

    public static async Task CreateAsync(string name, string type, Dictionary<string, string> parameters)
    {
        var client = await RcloneService.GetClientAsync().ConfigureAwait(false);
        var body = new Dictionary<string, object>
        {
            ["name"]       = name,
            ["type"]       = type,
            ["parameters"] = parameters,
        };
        using var _ = await client.PostAsync("config/create", body).ConfigureAwait(false);
    }

    public static async Task DeleteAsync(string name)
    {
        var client = await RcloneService.GetClientAsync().ConfigureAwait(false);
        var body = new Dictionary<string, object> { ["name"] = name };
        using var _ = await client.PostAsync("config/delete", body).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs `rclone authorize &lt;type&gt;` as a subprocess. rclone opens the browser,
    /// captures the OAuth redirect, and prints the resulting token JSON to stdout.
    /// Returns the JSON token string ready to be passed as the "token" parameter to config/create.
    /// </summary>
    public static Task<string> AuthorizeAsync(string type, CancellationToken ct = default)
    {
        var binary = RcloneService.BinaryPath
            ?? new RcloneBinaryManager().FindInstalled()
            ?? throw new InvalidOperationException("rclone is not installed.");

        return Task.Run(() =>
        {
            var psi = new ProcessStartInfo
            {
                FileName = binary,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("authorize");
            psi.ArgumentList.Add(type);

            var proc = Process.Start(psi)
                ?? throw new InvalidOperationException($"Failed to start: {binary} authorize {type}");

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            proc.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
            proc.ErrorDataReceived  += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            while (!proc.HasExited)
            {
                if (ct.IsCancellationRequested)
                {
                    try { proc.Kill(entireProcessTree: true); } catch { }
                    ct.ThrowIfCancellationRequested();
                }
                Thread.Sleep(100);
            }

            var combined = stdout.ToString() + "\n" + stderr.ToString();
            if (proc.ExitCode != 0 && !TryExtractToken(combined, out _))
                throw new InvalidOperationException(
                    $"rclone authorize exited with code {proc.ExitCode}.\n{combined}");

            if (!TryExtractToken(combined, out var token))
                throw new InvalidOperationException(
                    $"rclone authorize finished but no token was found in output:\n{combined}");

            return token;
        }, ct);
    }

    private static bool TryExtractToken(string output, out string token)
    {
        // Expected envelope:
        //   Paste the following into your remote machine --->
        //   {"access_token":"...",...}
        //   <---End paste
        var m = Regex.Match(output, @"--->\s*(\{.*?\})\s*<---", RegexOptions.Singleline);
        if (m.Success)
        {
            token = m.Groups[1].Value.Trim();
            return true;
        }

        // Fallback: look for any standalone {"access_token":...} JSON object.
        var m2 = Regex.Match(output, @"\{\s*""access_token""[^{}]*\}", RegexOptions.Singleline);
        if (m2.Success)
        {
            token = m2.Value.Trim();
            return true;
        }

        token = "";
        return false;
    }

    private static string GetStr(JsonElement el, string name)
        => el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";

    private static bool GetBool(JsonElement el, string name)
        => el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;

    private static int GetInt(JsonElement el, string name)
        => el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : 0;

    private static List<ProviderExample> GetExamples(JsonElement el)
    {
        var result = new List<ProviderExample>();
        if (!el.TryGetProperty("Examples", out var arr) || arr.ValueKind != JsonValueKind.Array) return result;
        foreach (var e in arr.EnumerateArray())
            result.Add(new ProviderExample(GetStr(e, "Value"), GetStr(e, "Help")));
        return result;
    }
}
