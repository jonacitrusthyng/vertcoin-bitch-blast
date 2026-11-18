using System.ComponentModel;
using System.IO;
using System.Diagnostics;
using System.Text.Json;

namespace IdleGpu;

internal enum AiState { Idle, Busy, Unknown }

internal static class InferenceProbe
{
    private enum Host { Studio, Bionic }

    internal static string FindHome()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string pointer = Path.Combine(profile, ".lmstudio-home-pointer");
        string legacy = Path.Combine(profile, ".cache", "lm-studio");
        return File.Exists(pointer) ? File.ReadAllText(pointer).Trim() :
            Directory.Exists(legacy) ? legacy : Path.Combine(profile, ".lmstudio");
    }

    // The default probe only attaches to existing app instances. Plain "lms ps" can start one.
    public static async Task<AiState> ReadAsync(string cli, CancellationToken token, bool customCli = false)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            if (customCli)
                return await QueryAsync(cli, null, null, token);

            var running = new Dictionary<int, Host>();
            foreach (Process process in Process.GetProcesses())
            {
                using (process)
                {
                    var host = process.ProcessName.ToLowerInvariant() switch
                    {
                        "lm studio" or "lmstudio" or "llmster" => Host.Studio,
                        "bionic" or "lm studio bionic" => Host.Bionic,
                        _ => (Host?)null
                    };
                    if (host.HasValue)
                        running.Add(process.Id, host.Value);
                }
            }
            if (running.Count == 0)
                return AiState.Idle;

            string home = FindHome();
            var records = new (string? Path, Host? ConventionalOwner)[]
            {
                (Path.Combine(home, ".internal", "http-server.json"), Host.Studio),
                (Path.Combine(home, "apps", "bionic", ".internal", "http-server.json"), Host.Bionic),
                (Environment.GetEnvironmentVariable("LMS_API_SERVER_INFO_PATH"), null)
            };

            var covered = new HashSet<Host>();
            var ports = new Dictionary<int, string>();
            foreach (var (path, conventionalOwner) in records)
            {
                if (path == null || !File.Exists(path))
                    continue;
                using var record = JsonDocument.Parse(await File.ReadAllTextAsync(path, token));
                var root = record.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    continue;
                Host? owner = conventionalOwner;
                if (root.TryGetProperty("pid", out var pid))
                {
                    if (pid.ValueKind != JsonValueKind.Number || !pid.TryGetInt32(out int id)
                        || !running.TryGetValue(id, out var liveOwner))
                        continue; // This record belongs to an exited or unrelated process.
                    owner = liveOwner;
                }
                if (!owner.HasValue || !running.ContainsValue(owner.Value)
                    || !root.TryGetProperty("port", out var port) || port.ValueKind != JsonValueKind.Number
                    || !port.TryGetInt32(out int number) || number is < 1 or > 65535)
                    continue;
                covered.Add(owner.Value);
                ports.TryAdd(number, path);
            }

            // If a running host has not published a reachable API yet, let it keep the GPU.
            if (!covered.IsSupersetOf(running.Values))
                return AiState.Unknown;
            AiState[] states = await Task.WhenAll(ports.Select(pair => QueryAsync(cli, pair.Key, pair.Value, token)));
            return states.Contains(AiState.Busy) ? AiState.Busy :
                states.Contains(AiState.Unknown) ? AiState.Unknown : AiState.Idle;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException
            or Win32Exception or InvalidOperationException or ArgumentException)
        {
            return AiState.Unknown;
        }
    }

    private static async Task<AiState> QueryAsync(string cli, int? port, string? record, CancellationToken token)
    {
        var start = new ProcessStartInfo(cli)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("ps");
        start.ArgumentList.Add("--json");
        if (port.HasValue)
        {
            start.ArgumentList.Add("--port");
            start.ArgumentList.Add(port.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            start.Environment["LMS_API_SERVER_INFO_PATH"] = record;
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var process = Process.Start(start);
        if (process == null)
            return AiState.Unknown;
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errors = process.StandardError.BaseStream.CopyToAsync(Stream.Null, timeout.Token);
            await Task.WhenAll(output, errors, process.WaitForExitAsync(timeout.Token)).WaitAsync(timeout.Token);
            return process.ExitCode == 0 ? Parse(await output) : AiState.Unknown;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            return AiState.Unknown;
        }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { } // Exited between observation and termination.
            }
        }
    }

    internal static AiState Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return AiState.Unknown;
        AiState result = AiState.Idle;
        foreach (var model in doc.RootElement.EnumerateArray())
        {
            if (model.ValueKind != JsonValueKind.Object)
            {
                result = AiState.Unknown;
                continue;
            }
            // LM Link sets deviceIdentifier to the peer's identifier for remote instances.
            if (model.TryGetProperty("deviceIdentifier", out var device) && device.ValueKind == JsonValueKind.String)
                continue;
            if (!model.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.String
                || !model.TryGetProperty("queued", out var queued) || queued.ValueKind != JsonValueKind.Number
                || !queued.TryGetInt32(out int count) || count < 0)
            {
                result = AiState.Unknown;
                continue;
            }
            if (count > 0 || status.GetString() is "processingPrompt" or "generating" or "computingEmbedding" or "computingDecision")
                return AiState.Busy;
            if (status.GetString() != "idle")
                result = AiState.Unknown;
        }
        return result;
    }
}
