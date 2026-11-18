using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using IdleGpu;
namespace VertcoinOcm;

internal sealed class IdlePolicy
{
    private TimeSpan? since;
    public bool ShouldMine(bool automatic, AiState ai, bool conflict, TimeSpan now, int delay)
    {
        if (conflict || automatic && ai != AiState.Idle) { since = null; return false; }
        if (!automatic) return true;
        since ??= now;
        return now - since >= TimeSpan.FromSeconds(delay);
    }
}
internal record MiningStatus(string State, string Detail, bool Active, double Hashrate = 0, int Accepted = 0, int Total = 0, string Ai = "Checking", string Log = "");
internal sealed class MinerStats
{
    private readonly Dictionary<string,double> rates = new();
    public double Hashrate { get; private set; }
    public int Accepted { get; private set; }
    public int Total { get; private set; }
    public string LastError { get; private set; } = "";
    public bool MonitoringUnavailable { get; private set; }
    public void Read(string line)
    {
        if(line.Contains("Monitoring has been disabled",StringComparison.OrdinalIgnoreCase)) MonitoringUnavailable=true;
        var rate = Regex.Match(line, @"((?:cl|cu)_device\(\d+\)):.*hashrate:\s*([\d.]+)\s*([kMG]?)H/s", RegexOptions.IgnoreCase);
        if (rate.Success && double.TryParse(rate.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double n))
        {
            rates[rate.Groups[1].Value] = n * (rate.Groups[3].Value.ToUpperInvariant() switch { "M" => 1000, "G" => 1000000, "" => 0.001, _ => 1 });
            Hashrate = rates.Values.Sum();
        }
        var shares = Regex.Match(line, @"accepted:\s*(\d+)/(\d+)");
        if(shares.Success) { Accepted=int.Parse(shares.Groups[1].Value); Total=int.Parse(shares.Groups[2].Value); LastError = ""; }
        if (line.Contains("ERROR") || line.Contains("failed", StringComparison.OrdinalIgnoreCase)) LastError = line;
        if (line.Contains("new block", StringComparison.OrdinalIgnoreCase) || line.Contains("new job", StringComparison.OrdinalIgnoreCase)) LastError = "";
    }
}
internal sealed class MiningController : IAsyncDisposable
{
    private CancellationTokenSource? cancel;
    private Task? task;
    internal Func<Settings,CancellationToken,Task<AiState>> Probe { get; init; } = ReadAiAsync;
    public bool Enabled => task is { IsCompleted: false };
    public event Action<MiningStatus>? Changed;
    public void Start(Settings settings)
    {
        if (Enabled) return;
        var args = settings.MinerArguments(); cancel?.Dispose(); cancel = new();
        task = Task.Run(() => Loop(settings.Copy(), args, cancel.Token));
    }
    public async Task StopAsync()
    {
        cancel?.Cancel(); if(task != null) await task;
        Changed?.Invoke(new("Mining stopped", "The GPU is yours. Start mining whenever you’re ready.", false, Ai:"Not monitoring"));
    }
    private async Task Loop(Settings settings, string[] args, CancellationToken ct)
    {
        using var ownership = new Mutex(false, @"Local\VertcoinIdleGpuController", out bool first);
        if(!first) { Changed?.Invoke(new("Controller already running", "Close the other Vertcoin miner before starting this one.", false)); return; }
        using var worker = new MinerWorker(settings.MinerPath, args);
        var policy = new IdlePolicy(); var watch = Stopwatch.StartNew(); var stats = new MinerStats();
        bool launched = false; DateTime started = DateTime.MinValue; string? logFile = null; long position=0; string pending=""; var oldLogs=new HashSet<string>(); var lines = new Queue<string>();
        try
        {
            while(true)
            {
                ct.ThrowIfCancellationRequested();
                var ai = settings.AutoAi ? await Probe(settings, ct) : AiState.Idle;
                ct.ThrowIfCancellationRequested();
                bool conflict = Conflict(worker.ProcessId, settings.MinerPath);
                if (launched)
                {
                    ReadLog();
                    if (!worker.Running) throw new IOException("VerthashMiner exited. " + (stats.LastError == "" ? "Check Activity for the miner’s last message." : stats.LastError));
                }
                bool should = policy.ShouldMine(settings.AutoAi, ai, conflict, watch.Elapsed, settings.IdleSeconds);
                if(!should && launched) { worker.Stop(); launched=false; ReadLog(); }
                if(should && !launched)
                {
                    started=DateTime.UtcNow; logFile=null; position=0; pending=""; stats=new();
                    oldLogs=Directory.EnumerateFiles(Path.GetDirectoryName(settings.MinerPath)!, "????-??-?? ??-??-??.txt").ToHashSet();
                    worker.Start(); launched=true;
                }
                ReadLog();
                string title = conflict ? "Paused · another miner" : !should ? ai == AiState.Busy ? "Paused · AI has the GPU" : ai == AiState.Unknown ? "Paused · checking AI" : "Waiting for idle GPU" : stats.LastError != "" ? "Miner needs attention" : stats.Hashrate > 0 ? "Mining active" : "Starting miner";
                string detail = conflict ? "Close the other miner to resume automatically." : !should ? ai == AiState.Busy ? "Mining will resume after your local AI finishes." : ai == AiState.Unknown ? "AI status is unavailable. Mining stays paused until it can be checked." : $"Waiting for {settings.IdleSeconds} seconds of continuous AI idle time." : stats.LastError != "" ? stats.LastError : stats.Hashrate > 0 ? $"{settings.Pool.Name} · payouts to your chosen VTC address" : "Verifying Verthash data and connecting to the selected pool…";
                if(launched && stats.MonitoringUnavailable) detail += " Temperature monitoring is unavailable; the miner cannot enforce its temperature limit on this GPU.";
                Changed?.Invoke(new(title, detail, launched, launched ? stats.Hashrate : 0, stats.Accepted, stats.Total, settings.AutoAi ? ai.ToString() : "Manual mining", string.Join(Environment.NewLine,lines)));
                await Task.Delay(1000,ct);
            }
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested) { }
        catch(Exception e) { Changed?.Invoke(new("Mining stopped · check setup", e.Message, false, Ai:"Stopped", Log:string.Join(Environment.NewLine,lines))); }
        finally { worker.Stop(); }

        void ReadLog()
        {
            if (started == DateTime.MinValue) return;
            try
            {
                logFile ??= Directory.EnumerateFiles(Path.GetDirectoryName(settings.MinerPath)!, "????-??-?? ??-??-??.txt")
                    .Where(p => !oldLogs.Contains(p)).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                if(logFile == null) return;
                using var file = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if(file.Length < position) position=0;
                file.Seek(position, SeekOrigin.Begin); using var reader=new StreamReader(file);
                string text=pending+reader.ReadToEnd(); position=file.Position;
                int last=text.LastIndexOf('\n'); if(last<0){pending=text;return;} pending=text[(last+1)..];
                foreach(string line in text[..last].Split('\n',StringSplitOptions.RemoveEmptyEntries))
                {
                    stats.Read(line); lines.Enqueue(line.TrimEnd()); if(lines.Count>200) lines.Dequeue();
                }
            }
            catch(IOException) { }
        }
    }
    internal static async Task<AiState> ReadAiAsync(Settings settings, CancellationToken ct)
    {
        // For hosts without inference-status APIs, a running process reserves the GPU.
        string[] watched=settings.PauseProcesses.Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Select(Path.GetFileNameWithoutExtension).OfType<string>().ToArray();
        foreach(var p in Process.GetProcesses())
        {
            using(p) { try { if(watched.Contains(p.ProcessName,StringComparer.OrdinalIgnoreCase)) return AiState.Busy; } catch(InvalidOperationException){} }
        }
        return await InferenceProbe.ReadAsync(Path.Combine(InferenceProbe.FindHome(),"bin","lms.exe"),ct);
    }
    private static bool Conflict(int? pid,string exe)
    {
        foreach(var p in Process.GetProcesses())
        {
            using(p) { try { if(p.Id!=pid && (p.ProcessName.StartsWith("vertcoin-ocm",StringComparison.OrdinalIgnoreCase) || p.ProcessName.Equals(Path.GetFileNameWithoutExtension(exe),StringComparison.OrdinalIgnoreCase))) return true; } catch(InvalidOperationException){} }
        }
        return false;
    }
    public async ValueTask DisposeAsync() { await StopAsync(); cancel?.Dispose(); }
}
