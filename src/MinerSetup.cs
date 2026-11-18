using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IdleGpu;
namespace VertcoinOcm;
public static class MinerSetup
{
    public const long DataBytes = 1283457024;
    public const string DataHash = "A55531E843CD56B010114AAF6325B0D529ECF88F8AD47639B6EDEDAFD721AA48";
    public const string DownloadUrl = "https://github.com/CryptoGraphics/VerthashMiner/releases/download/0.7.2/VerthashMiner-0.7.2-CUDA11-windows.zip";
    public const string ArchiveHash = "E5AF5AB19154F6EE57DB22645A43200336E8017865C71505AE669AF19243449E";
    public static string DefaultExe => Path.Combine(Settings.DataDir, "miner-0.7.2", "VerthashMiner-0.7.2-CUDA11-windows", "VerthashMiner.exe");
    public static string DefaultData => Path.Combine(Settings.DataDir, "verthash.dat");
    public static async Task<string> InstallAsync(IProgress<string> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(Settings.DataDir);
        if (File.Exists(DefaultExe)) return DefaultExe;
        progress.Report("Downloading VerthashMiner 0.7.2 from its official GitHub release…");
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        byte[] zip = await http.GetByteArrayAsync(DownloadUrl, ct);
        if (Convert.ToHexString(SHA256.HashData(zip)) != ArchiveHash) throw new InvalidDataException("The miner download does not match the pinned SHA-256. Installation stopped.");
        progress.Report("Download verified. Installing the miner…");
        using var stream = new MemoryStream(zip); using var archive = new ZipArchive(stream);
        archive.ExtractToDirectory(Path.Combine(Settings.DataDir, "miner-0.7.2"), true);
        if (!File.Exists(DefaultExe)) throw new FileNotFoundException("The release did not contain VerthashMiner.exe.");
        return DefaultExe;
    }
    public static async Task GenerateDataAsync(string exe, string destination, IProgress<string> progress, CancellationToken ct)
    {
        string temporary = destination + ".generating";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(destination))!);
        if (drive.AvailableFreeSpace < DataBytes + 128 * 1024 * 1024) throw new IOException("The Verthash data file needs at least 1.4 GB of free disk space.");
        if (File.Exists(temporary)) File.Delete(temporary);
        using var worker = new MinerWorker(exe, "--gen-verthash-data", temporary);
        try
        {
            worker.Start();
            while (worker.Running)
            {
                ct.ThrowIfCancellationRequested();
                long size = File.Exists(temporary) ? new FileInfo(temporary).Length : 0;
                progress.Report($"Growing the Verthash data file… {Math.Min(100, size * 100 / DataBytes)}% · this can take several minutes");
                await Task.Delay(1000, ct);
            }
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(temporary) || new FileInfo(temporary).Length != DataBytes) throw new IOException("Verthash data generation did not finish. See the miner log or try again.");
            progress.Report("Verifying the completed Verthash data file…");
            await using(var dataStream=File.OpenRead(temporary))
                if(Convert.ToHexString(await SHA256.HashDataAsync(dataStream,ct))!=DataHash)
                    throw new InvalidDataException("Generated Verthash data did not match its canonical hash. Try setup again.");
            File.Move(temporary, destination, true);
            progress.Report("Verthash data file verified and ready.");
        }
        finally { worker.Stop(); if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static async Task<List<Gpu>> ScanAsync(string exe, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var start = new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe), UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("--device-list"); using var p = Process.Start(start) ?? throw new IOException("Cannot start device detection.");
        try { var stdout = p.StandardOutput.ReadToEndAsync(timeout.Token); var stderr = p.StandardError.ReadToEndAsync(timeout.Token); await Task.WhenAll(stdout, stderr, p.WaitForExitAsync(timeout.Token)); if(p.ExitCode != 0) throw new IOException("GPU detection failed: " + await stderr); return Gpu.Parse(await stdout + "\n" + await stderr); }
        finally { if (!p.HasExited) p.Kill(true); }
    }
    public static async Task<string> BenchmarkAsync(string exe, string data, Gpu gpu, int intensity, int temperature, CancellationToken ct)
    {
        if(!File.Exists(data) || new FileInfo(data).Length!=DataBytes) throw new IOException("Prepare the Verthash data file before testing a GPU.");
        foreach(var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)))
        { using(p) { throw new IOException("Another VerthashMiner process is running. Stop it before testing a GPU."); } }
        string directory=Path.GetDirectoryName(exe)!;
        var before=Directory.EnumerateFiles(directory,"????-??-?? ??-??-??.txt").ToHashSet();
        // v0.7.2 validates GBT fields even in offline mode. --benchmark bypasses
        // network work; loopback also prevents any accidental external destination.
        // This checksum-valid address encodes version 71 + twenty ZERO bytes. It is
        // only an offline test vector, never a live pool payout address or a wallet.
        const string nullTestAddress="VZzpuiATQouD4mu9jnedRuKrgpHyY2Me1w";
        using var worker=new MinerWorker(exe,"--benchmark","--url","http://127.0.0.1:1","--user","benchmark","--pass","x","--coinbase-addr",nullTestAddress,"--verthash-data",data,gpu.Backend=="cu"?"--cu-devices":"--cl-devices",$"{gpu.Index}:b50:o{intensity}:m1:t{temperature}","--log-file");
        var stats=new MinerStats(); var watch=Stopwatch.StartNew();
        worker.Start();
        try
        {
            while(watch.Elapsed<TimeSpan.FromSeconds(35))
            {
                await Task.Delay(1000,ct);
                string? log=Directory.EnumerateFiles(directory,"????-??-?? ??-??-??.txt").FirstOrDefault(p=>!before.Contains(p));
                if(log!=null)
                {
                    using var stream=new FileStream(log,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);using var reader=new StreamReader(stream);
                    foreach(string line in (await reader.ReadToEndAsync(ct)).Split('\n')) stats.Read(line);
                }
                if(stats.Hashrate>0) return $"GPU test passed · {gpu.Name} · {stats.Hashrate:N2} kH/s (short offline sample, not a pool earnings estimate)." + (stats.MonitoringUnavailable?" Temperature monitoring is unavailable; the miner cannot enforce its temperature limit on this GPU.":"");
                if(!worker.Running) throw new IOException("GPU test failed. "+(stats.LastError==""?"The miner exited; inspect its local log.":stats.LastError));
            }
            throw new IOException("No hashrate reported within 35 seconds. "+(stats.LastError==""?"Inspect the miner log and try another GPU or driver.":stats.LastError));
        }
        finally { worker.Stop(); }
    }
    public static async Task<string> CheckPoolAsync(string url, CancellationToken ct)
    {
        var uri = Settings.ValidatePool(url);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var watch = Stopwatch.StartNew(); using var tcp = new TcpClient();
        await tcp.ConnectAsync(uri.Host, uri.Port, timeout.Token);
        var stream = tcp.GetStream();
        await stream.WriteAsync(Encoding.UTF8.GetBytes("{\"id\":1,\"method\":\"mining.subscribe\",\"params\":[\"VertcoinOneClickMiner/0.3\"]}\n"), timeout.Token);
        var buffer = new byte[1]; var line = new StringBuilder();
        while(line.Length < 65536)
        {
            if (await stream.ReadAsync(buffer, timeout.Token) == 0) throw new IOException("Pool closed the connection before answering.");
            if (buffer[0] != 10) { line.Append((char)buffer[0]); continue; }
            using var doc = JsonDocument.Parse(line.ToString()); line.Clear();
            if (!doc.RootElement.TryGetProperty("id", out var id) || id.ToString() != "1") continue;
            if (doc.RootElement.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null) throw new IOException("Pool rejected the subscribe request: " + error);
            if (!doc.RootElement.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array) throw new IOException("Pool did not return a valid Stratum subscription.");
            return $"Stratum responding · {watch.ElapsedMilliseconds} ms · payout authorization checked when mining starts";
        }
        throw new IOException("Unexpectedly large response from pool.");
    }
}
