using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;

namespace VertcoinOcm;

public static class CoreSetup
{
    public const string Version = "23.2";
    public const string ReleaseUrl = "https://github.com/vertcoin-project/vertcoin-core/releases/tag/v23.2";
    public const string DownloadUrl = "https://github.com/vertcoin-project/vertcoin-core/releases/download/v23.2/vertcoin-23.2-win64.zip";
    public const string ArchiveHash = "FC710F8603A0C8366E08B78EE1DD569AC5FFF3EA485317F9172FFEFE140AB5E2";
    public static string InstallDirectory => Path.Combine(Settings.DataDir, "core-23.2");
    public static string DefaultExe => Path.Combine(InstallDirectory, "vertcoin-23.2", "bin", "vertcoind.exe");

    public static async Task PrepareVerthashAsync(CoreNodeSettings settings, string minerData, IProgress<string> progress, CancellationToken ct)
    {
        settings.Validate(true);
        string destination = Path.Combine(settings.NetworkDirectory, "verthash.dat");
        if (File.Exists(destination) || !File.Exists(minerData) || new FileInfo(minerData).Length != MinerSetup.DataBytes) return;
        progress.Report("Verifying your existing Verthash data before reusing a separate copy for Core…");
        await using var source = new FileStream(minerData, FileMode.Open, FileAccess.Read, FileShare.Read, 1048576, true);
        if (Convert.ToHexString(await SHA256.HashDataAsync(source, ct)) != MinerSetup.DataHash)
            throw new InvalidDataException("The existing miner data failed its checksum. Prepare valid miner data, or select a different file before starting Core.");
        Directory.CreateDirectory(settings.NetworkDirectory);
        var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(settings.NetworkDirectory))!);
        if (drive.AvailableFreeSpace < MinerSetup.DataBytes + 512L * 1024 * 1024)
            throw new IOException("Core needs space for its own 1.28 GB Verthash file, plus chain state, block data and wallets.");
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".preparing";
        try
        {
            progress.Report("Copying verified Verthash data into Core’s separate data folder…");
            source.Position = 0;
            await using (var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1048576, true))
                await source.CopyToAsync(target, ct);
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, destination, false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static async Task<string> InstallAsync(IProgress<string> progress, CancellationToken ct)
    {
        progress.Report("Downloading official Vertcoin Core 23.2 (about 32 MB)…");
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(4) };
        var bytes = await http.GetByteArrayAsync(DownloadUrl, ct);
        if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(ArchiveHash, StringComparison.Ordinal))
            throw new InvalidDataException("The Core download did not match its pinned SHA-256. Nothing was installed.");
        ct.ThrowIfCancellationRequested();
        progress.Report("Download verified. Installing Core’s node and wallet programs…");
        Directory.CreateDirectory(InstallDirectory);
        using var stream = new MemoryStream(bytes);
        using var zip = new ZipArchive(stream);
        // ExtractToDirectory rejects traversal outside the target. The whole archive is pinned above.
        zip.ExtractToDirectory(InstallDirectory, true);
        if (!File.Exists(DefaultExe)) throw new FileNotFoundException("The official archive did not contain vertcoind.exe.");
        progress.Report("Core is installed. Start the node when you’re ready; the first blockchain sync can take a while.");
        return DefaultExe;
    }
}
