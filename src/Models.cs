using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace VertcoinOcm;

public record Pool(string Id, string Name, string Url, string Password, string Fee, string Website, bool WorkerSuffix = false)
{
    public override string ToString() => Name;
    public static readonly Pool[] Catalog = [
        new("woolypooly", "WoolyPooly · PPLNS", "stratum+tcp://pool.woolypooly.com:3102", "x", "0.9%", "https://woolypooly.com/en/coin/vtc", true),
        new("zpool", "Zpool · VTC payout", "stratum+tcp://verthash.mine.zpool.ca:6144", "c=VTC,zap=VTC", "See pool terms", "https://zpool.ca/"),
        new("suprnova", "Suprnova · anonymous VTC", "stratum+tcp://vtc.suprnova.cc:1776", "x", "1%", "https://vtc.suprnova.cc/"),
        new("custom", "Custom pool…", "", "x", "Pool dependent", "https://vertcoin.io/vertcoin-miners-and-pools-list/")
    ];
}
public record Gpu(string Backend, int Index, string Name)
{
    public override string ToString() => $"{Name} · {Backend.ToUpperInvariant()} {Index}";
    public string Key => $"{Backend}:{Index}";
    public static List<Gpu> Parse(string output)
    {
        var result = new List<Gpu>(); string backend = "";
        foreach (var line in output.Split('\n'))
        {
            if (line.Contains("OpenCL devices:")) backend = "cl";
            if (line.Contains("CUDA devices:")) backend = "cu";
            var m = Regex.Match(line, @"Index:\s*(\d+)\. Name:\s*(.+?)(?:\. pcieId:.*)?$");
            if (m.Success && backend.Length > 0) result.Add(new(backend, int.Parse(m.Groups[1].Value), m.Groups[2].Value.Trim()));
        }
        return result;
    }
}
public class Settings
{
    public AudioPreferences Audio { get; set; } = new();
    public CoreNodeSettings Core { get; set; } = new();
    public string Address { get; set; } = "";
    public string PoolId { get; set; } = "woolypooly";
    public string CustomUrl { get; set; } = "";
    public string CustomPassword { get; set; } = "x";
    public string WorkerName { get; set; } = "ocm";
    public string MinerPath { get; set; } = "";
    public string DataPath { get; set; } = "";
    public string GpuKey { get; set; } = "";
    public bool AutoAi { get; set; } = true;
    public int IdleSeconds { get; set; } = 30;
    public int Occupancy { get; set; } = 75;
    public int Temperature { get; set; } = 75;
    public string PauseProcesses { get; set; } = "ollama,ollama_llama_server,llama-server,ComfyUI";
    // Preserve existing settings and the 1.28 GB data file across the branding update.
    public static string DataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VertcoinGarden");
    public static string FilePath => Path.Combine(DataDir, "settings.json");
    public Pool Pool => VertcoinOcm.Pool.Catalog.FirstOrDefault(p => p.Id == PoolId) ?? VertcoinOcm.Pool.Catalog[0];
    public string PoolUrl => PoolId == "custom" ? CustomUrl : Pool.Url;
    public string PoolPassword => PoolId == "custom" ? CustomPassword : Pool.Password;
    public static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
    public void Save()
    {
        Directory.CreateDirectory(DataDir);
        string temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, FilePath, true);
    }
    public Settings Copy() => (Settings)MemberwiseClone();
    public void Validate()
    {
        if (!AddressValidator.Valid(Address)) throw new ArgumentException("Enter a valid mainnet Vertcoin receiving address. Check the whole address, including its checksum.");
        ValidatePool(PoolUrl);
        if (!Regex.IsMatch(WorkerName, @"^[A-Za-z0-9_-]{1,32}$")) throw new ArgumentException("Use 1–32 letters, numbers, underscores or hyphens for the worker name.");
        if (PoolPassword.Length > 256 || PoolPassword.Any(char.IsControl)) throw new ArgumentException("The pool password is invalid.");
        if (IdleSeconds is < 5 or > 3600 || Occupancy is < 1 or > 100 || Temperature is < 50 or > 90) throw new ArgumentException("Check idle delay (5–3600), GPU intensity (1–100) and temperature (50–90).");
        if (!Regex.IsMatch(GpuKey, @"^(cl|cu):\d+$")) throw new ArgumentException("Choose a GPU in Settings. Use Scan GPUs after preparing the miner.");
        if (!File.Exists(MinerPath)) throw new FileNotFoundException("Prepare the miner in Settings or select your VerthashMiner.exe.");
        if (!File.Exists(DataPath) || new FileInfo(DataPath).Length != MinerSetup.DataBytes) throw new FileNotFoundException("Prepare the Verthash data file in Settings, or select an existing valid verthash.dat.");
    }
    public static Uri ValidatePool(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "stratum+tcp" || uri.Port is < 1 or > 65535 || string.IsNullOrWhiteSpace(uri.Host) || uri.UserInfo != "" || uri.Query != "" || uri.Fragment != "" || uri.AbsolutePath != "/")
            throw new ArgumentException("Use a pool endpoint in this format: stratum+tcp://hostname:port");
        return uri;
    }
    public string[] MinerArguments()
    {
        Validate(); var parts = GpuKey.Split(':');
        string user = Pool.WorkerSuffix && PoolId != "custom" ? $"{Address}.{WorkerName}" : Address;
        return ["--url", PoolUrl, "--user", user, "--pass", PoolPassword, "--verthash-data", DataPath,
            parts[0] == "cu" ? "--cu-devices" : "--cl-devices", $"{parts[1]}:b50:o{Occupancy}:m1:t{Temperature}",
            "--retries", "5", "--retry-pause", "10", "--timeout", "30", "--no-redirect", "--log-file"];
    }
}
public static class AddressValidator
{
    public static bool Valid(string address)
    {
        if (string.IsNullOrEmpty(address) || address.Length > 90 || address.Trim() != address) return false;
        if (address.StartsWith("vtc1", StringComparison.OrdinalIgnoreCase)) return Witness(address);
        if (address.Length is < 26 or > 35) return false;
        const string alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
        BigInteger n = 0;
        foreach (char c in address) { int v = alphabet.IndexOf(c); if (v < 0) return false; n = n * 58 + v; }
        byte[] body = n.ToByteArray(true, true);
        var bytes = new byte[address.TakeWhile(c => c == '1').Count() + body.Length]; body.CopyTo(bytes, bytes.Length - body.Length);
        if (bytes.Length != 25 || bytes[0] is not (71 or 5)) return false;
        var hash = SHA256.HashData(SHA256.HashData(bytes.AsSpan(0, 21)));
        return bytes.AsSpan(21, 4).SequenceEqual(hash.AsSpan(0, 4));
    }
    private static bool Witness(string s)
    {
        if (s.Any(char.IsUpper) && s.Any(char.IsLower)) return false;
        s = s.ToLowerInvariant(); string payload = s[4..];
        if (payload.Length < 7) return false;
        const string alphabet = "qpzry9x8gf2tvdw0s3jn54khce6mua7l";
        int[] data = payload.Select(c => alphabet.IndexOf(c)).ToArray(); if (data.Any(x => x < 0) || data[0] > 16) return false;
        uint chk = 1; uint[] generator = [0x3b6a57b2,0x26508e6d,0x1ea119fa,0x3d4233dd,0x2a1462b3];
        int[] values = "vtc".Select(c => c >> 5).Concat([0]).Concat("vtc".Select(c => c & 31)).Concat(data).ToArray();
        foreach (int value in values) { uint top = chk >> 25; chk = ((chk & 0x1ffffff) << 5) ^ (uint)value; for (int i=0;i<5;i++) if (((top >> i)&1)!=0) chk ^= generator[i]; }
        if (chk != (data[0] == 0 ? 1u : 0x2bc830a3u)) return false;
        int bits = 0, acc = 0, count = 0;
        foreach(int v in data.Skip(1).Take(data.Length-7)) { acc = ((acc << 5) | v) & 4095; bits += 5; if(bits>=8) { bits-=8; count++; } }
        if (bits >= 5 || ((acc << (8-bits)) & 255) != 0) return false;
        return count is >= 2 and <= 40 && (data[0] != 0 || count is 20 or 32);
    }
}
