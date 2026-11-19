using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VertcoinOcm;

public sealed class CoreNodeSettings
{
    public string ExecutablePath { get; set; } = "";
    public string DataDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VertcoinBitchBlast", "Core");
    public int RpcPort { get; set; } = 5888;
    public int PeerPort { get; set; } = 5889;
    public int PruneMiB { get; set; } = 2048;
    public bool Listen { get; set; }
    public bool StopWithApp { get; set; } = true;
    public string WalletName { get; set; } = "BitchBlast";
    public string Network { get; set; } = "main";
    // Optional custom cookie for attaching to an already-running local Core. Never a password.
    public string CookieFile { get; set; } = "";

    public CoreNodeSettings Copy() => (CoreNodeSettings)MemberwiseClone();
    public string NetworkDirectory => Network == "regtest" ? Path.Combine(DataDirectory, "regtest") : DataDirectory;
    // Match Core's directory convention without creating a wallets folder that could hide a legacy wallet.dat.
    public string WalletDirectory => Directory.Exists(Path.Combine(NetworkDirectory, "wallets")) ? Path.Combine(NetworkDirectory, "wallets") : NetworkDirectory;
    public string CookiePath
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(CookieFile)) return CookieFile;
            try
            {
                string name = File.ReadAllText(Path.Combine(NetworkDirectory, "bitchblast-cookie-name.txt")).Trim();
                if (Regex.IsMatch(name, @"^bitchblast-[0-9a-f]{32}\.cookie$", RegexOptions.CultureInvariant)
                    && File.Exists(Path.Combine(NetworkDirectory, name))) return Path.Combine(NetworkDirectory, name);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            return Path.Combine(NetworkDirectory, ".cookie");
        }
    }
    public Uri RpcEndpoint => new($"http://127.0.0.1:{RpcPort}/");

    public void Validate(bool requireExecutable = false)
    {
        if (Network is not ("main" or "regtest")) throw new ArgumentException("Choose mainnet or the isolated regtest network.");
        ValidateLocalPath(DataDirectory, "Core data folder");
        if (!string.IsNullOrEmpty(CookieFile)) ValidateLocalPath(CookieFile, "Core authentication cookie");
        if (RpcPort is < 1024 or > 65535 || PeerPort is < 1024 or > 65535 || RpcPort == PeerPort)
            throw new ArgumentException("Use different RPC and peer ports between 1024 and 65535.");
        if (PruneMiB != 0 && PruneMiB is < 550 or > 1_048_576)
            throw new ArgumentException("Use 0 for archival storage, or a block-file target between 550 and 1048576 MiB.");
        ValidateWalletName(WalletName, allowUnnamed: true);
        if (requireExecutable)
        {
            ValidateLocalPath(ExecutablePath, "Vertcoin Core executable");
            string name = Path.GetFileName(ExecutablePath);
            if (!name.Equals("vertcoind.exe", StringComparison.OrdinalIgnoreCase) && !name.Equals("vertcoin-qt.exe", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Choose the official vertcoind.exe or vertcoin-qt.exe executable.");
            if (!File.Exists(ExecutablePath)) throw new FileNotFoundException("Prepare Vertcoin Core or choose its installed executable.");
        }
    }

    public static void ValidateWalletName(string wallet, bool allowUnnamed = false)
    {
        if (allowUnnamed && wallet == "") return;
        if (!Regex.IsMatch(wallet ?? "", @"^[A-Za-z0-9][A-Za-z0-9 _-]{0,63}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Use a wallet name of 1–64 letters, numbers, spaces, underscores or hyphens, starting with a letter or number.");
    }

    private static void ValidateLocalPath(string path, string label)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal) || path.Any(char.IsControl))
            throw new ArgumentException(label + " must be an absolute path on this computer.");
    }

    // A private, empty config file keeps unrelated vertcoin.conf options out of managed launches.
    public string[] LaunchArguments(string isolatedConfig, string ownedCookie)
    {
        Validate(true);
        ValidateLocalPath(isolatedConfig, "Managed configuration");
        ValidateLocalPath(ownedCookie, "Managed cookie");
        var args = new List<string>
        {
            "-server=1", "-daemon=0", "-nosettings", "-conf=" + isolatedConfig,
            "-datadir=" + DataDirectory, "-chain=" + Network,
            "-rpcbind=127.0.0.1", "-rpcallowip=127.0.0.1", "-rpcport=" + RpcPort,
            "-rpccookiefile=" + ownedCookie, "-port=" + PeerPort,
            "-prune=" + PruneMiB.ToString(CultureInfo.InvariantCulture),
            "-listen=" + (Listen && Network == "main" ? "1" : "0"),
            "-upnp=0", "-natpmp=0", "-listenonion=0", "-dbcache=256", "-verthash-diskonly=1",
            "-walletdir=" + WalletDirectory
        };
        if (Network == "regtest") args.AddRange(["-connect=0", "-dnsseed=0", "-discover=0", "-fixedseeds=0"]);
        if (WalletName.Length == 0 ? File.Exists(Path.Combine(WalletDirectory, "wallet.dat")) : Directory.Exists(Path.Combine(WalletDirectory, WalletName))) args.Add("-wallet=" + WalletName);
        return args.ToArray();
    }
}

public sealed record CoreNodeStatus(string Chain, int Blocks, int Headers, double VerificationProgress,
    bool InitialBlockDownload, int Connections, int Inbound, int Outbound, bool Pruned, long DiskBytes,
    int Version, string Subversion, bool NetworkActive, bool Owned);

public sealed record CoreWalletStatus(string Name, decimal Trusted, decimal UntrustedPending, decimal Immature,
    bool PrivateKeysEnabled, bool Encrypted, bool Locked, bool Descriptors, bool Scanning);

public sealed class CoreRpcException(int code, string message) : Exception(message)
{
    public int Code { get; } = code;
}

public sealed class CoreRpcClient : IDisposable
{
    private readonly CoreNodeSettings settings;
    private readonly HttpClient http;
    private int requestId;

    public CoreRpcClient(CoreNodeSettings settings, HttpMessageHandler? handler = null)
    {
        settings.Validate();
        this.settings = settings.Copy();
        http = new HttpClient(handler ?? new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false })
        { Timeout = TimeSpan.FromSeconds(10), MaxResponseContentBufferSize = 2 * 1024 * 1024 };
    }

    public static Uri ValidateEndpoint(Uri endpoint)
    {
        if (endpoint.Scheme != "http" || !IPAddress.TryParse(endpoint.Host, out var ip) || !IPAddress.IsLoopback(ip)
            || endpoint.UserInfo.Length != 0 || endpoint.AbsolutePath != "/" || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0)
            throw new ArgumentException("Core RPC must use a plain loopback IP address on this computer.");
        return endpoint;
    }

    internal async Task<JsonElement> CallAsync(string method, object parameters, string? wallet, CancellationToken cancellationToken)
    {
        Uri endpoint = ValidateEndpoint(settings.RpcEndpoint);
        if (wallet is not null)
        {
            CoreNodeSettings.ValidateWalletName(wallet, allowUnnamed: true);
            endpoint = new Uri(endpoint, "wallet/" + Uri.EscapeDataString(wallet));
        }
        string cookie;
        try { cookie = (await File.ReadAllTextAsync(settings.CookiePath, cancellationToken)).TrimEnd('\r', '\n'); }
        catch (IOException) { throw new InvalidOperationException("Core's local authentication cookie is not ready. Start Core and wait for its RPC service."); }
        if (cookie.Length > 1024 || !cookie.StartsWith("__cookie__:", StringComparison.Ordinal) || cookie.Length <= 11 || cookie.Any(char.IsControl))
            throw new InvalidDataException("The selected file is not a valid Core authentication cookie.");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(cookie)));
        request.Content = new StringContent(JsonSerializer.Serialize(new { jsonrpc = "1.0", id = Interlocked.Increment(ref requestId), method, @params = parameters }), Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new UnauthorizedAccessException("Core rejected local authentication. Check the data folder and RPC port.");
        if ((int)response.StatusCode is >= 300 and < 400) throw new InvalidOperationException("Core RPC redirects are not accepted.");
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        JsonDocument doc;
        try { doc = JsonDocument.Parse(body); }
        catch (JsonException) { throw new InvalidDataException("The local port did not return a valid Core RPC response."); }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
            {
                int code = error.TryGetProperty("code", out var c) ? c.GetInt32() : 0;
                string message = error.TryGetProperty("message", out var m) ? m.GetString() ?? "Core RPC failed." : "Core RPC failed.";
                throw new CoreRpcException(code, message.Length > 500 ? message[..500] : message);
            }
            response.EnsureSuccessStatusCode();
            if (!root.TryGetProperty("result", out var result)) throw new InvalidDataException("Core RPC response has no result.");
            return result.Clone();
        }
    }

    public async Task<CoreNodeStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var chain = await CallAsync("getblockchaininfo", Array.Empty<object>(), null, ct);
        var network = await CallAsync("getnetworkinfo", Array.Empty<object>(), null, ct);
        return new(Str(chain, "chain"), Int(chain, "blocks"), Int(chain, "headers"), Num(chain, "verificationprogress"),
            Bool(chain, "initialblockdownload"), Int(network, "connections"), Int(network, "connections_in"),
            Int(network, "connections_out"), Bool(chain, "pruned"), Long(chain, "size_on_disk"), Int(network, "version"),
            Str(network, "subversion"), Bool(network, "networkactive"), false);
    }

    public async Task<IReadOnlyList<string>> ListWalletsAsync(CancellationToken ct = default)
    {
        var result = await CallAsync("listwallets", Array.Empty<object>(), null, ct);
        return result.EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
    }

    public async Task<CoreWalletStatus> GetWalletAsync(string wallet, CancellationToken ct = default)
    {
        var info = await CallAsync("getwalletinfo", Array.Empty<object>(), wallet, ct);
        var balances = await CallAsync("getbalances", Array.Empty<object>(), wallet, ct);
        var mine = balances.GetProperty("mine");
        bool encrypted = info.TryGetProperty("unlocked_until", out var until);
        bool scanning = info.TryGetProperty("scanning", out var scan) && scan.ValueKind != JsonValueKind.False;
        return new(Str(info, "walletname"), Dec(mine, "trusted"), Dec(mine, "untrusted_pending"), Dec(mine, "immature"),
            Bool(info, "private_keys_enabled"), encrypted, encrypted && until.GetInt64() == 0, Bool(info, "descriptors"), scanning);
    }

    public async Task<string> GetNewAddressAsync(string wallet, CancellationToken ct = default)
        => (await CallAsync("getnewaddress", new[] { "Bitch Blast mining payouts", "bech32" }, wallet, ct)).GetString()
            ?? throw new InvalidDataException("Core returned no receiving address.");

    public async Task<string> CreateWalletAsync(string name, string passphrase, CancellationToken ct = default)
    {
        CoreNodeSettings.ValidateWalletName(name);
        if (string.IsNullOrWhiteSpace(passphrase) || passphrase.Length < 12)
            throw new ArgumentException("Use a wallet passphrase of at least 12 characters. Keep it somewhere safe; the app does not save it.");
        var result = await CallAsync("createwallet", new { wallet_name = name, disable_private_keys = false, blank = false,
            passphrase, avoid_reuse = false, descriptors = true, load_on_startup = false, external_signer = false }, null, ct);
        return Str(result, "name");
    }

    internal async Task StopAsync(CancellationToken ct) => _ = await CallAsync("stop", Array.Empty<object>(), null, ct);
    private static string Str(JsonElement e, string key) => e.TryGetProperty(key, out var p) ? p.GetString() ?? "" : "";
    private static int Int(JsonElement e, string key) => e.TryGetProperty(key, out var p) ? p.GetInt32() : 0;
    private static long Long(JsonElement e, string key) => e.TryGetProperty(key, out var p) ? p.GetInt64() : 0;
    private static double Num(JsonElement e, string key) => e.TryGetProperty(key, out var p) ? p.GetDouble() : 0;
    private static decimal Dec(JsonElement e, string key) => e.TryGetProperty(key, out var p) ? p.GetDecimal() : 0;
    private static bool Bool(JsonElement e, string key) => e.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.True;
    public void Dispose() => http.Dispose();
}

// Core owns durable chain/wallet data. Unlike the hashing worker, it must never be force-killed.
public sealed class CoreNodeManager : IDisposable
{
    private readonly SemaphoreSlim lifecycle = new(1, 1);
    private Process? owned;
    private CoreNodeSettings? launched;
    private string? isolatedConfig;
    private bool disposed;
    public bool IsOwnedRunning => owned is not null && !owned.HasExited;
    public int? OwnedProcessId => IsOwnedRunning ? owned!.Id : null;

    public async Task StartAsync(CoreNodeSettings settings, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        settings.Validate(true);
        await lifecycle.WaitAsync(ct);
        try
        {
            if (IsOwnedRunning) throw new InvalidOperationException("The Core process started by this app is already running.");
            ReleaseExited();
            // A busy RPC port is an existing service, not an invitation to start or stop it.
            using (var probe = new System.Net.Sockets.TcpClient())
            {
                try { await probe.ConnectAsync(IPAddress.Loopback, settings.RpcPort, ct); }
                catch (System.Net.Sockets.SocketException) { }
                if (probe.Connected) throw new InvalidOperationException("That RPC port is already in use. Refresh to inspect the existing node, or choose a different local RPC port.");
            }
            Directory.CreateDirectory(settings.DataDirectory);
            Directory.CreateDirectory(settings.NetworkDirectory);
            string nonce = Guid.NewGuid().ToString("N");
            isolatedConfig = Path.Combine(settings.DataDirectory, "bitchblast-" + nonce + ".conf");
            await File.WriteAllTextAsync(isolatedConfig, "# Empty configuration for a Bitch Blast managed Core process.\n", new UTF8Encoding(false), ct);
            launched = settings.Copy();
            launched.CookieFile = Path.Combine(settings.NetworkDirectory, "bitchblast-" + nonce + ".cookie");
            await File.WriteAllTextAsync(Path.Combine(settings.NetworkDirectory, "bitchblast-cookie-name.txt"), Path.GetFileName(launched.CookieFile), new UTF8Encoding(false), ct);
            var psi = new ProcessStartInfo(settings.ExecutablePath)
            {
                UseShellExecute = false,
                CreateNoWindow = Path.GetFileName(settings.ExecutablePath).Equals("vertcoind.exe", StringComparison.OrdinalIgnoreCase),
                WorkingDirectory = settings.DataDirectory
            };
            foreach (string arg in settings.LaunchArguments(isolatedConfig, launched.CookieFile)) psi.ArgumentList.Add(arg);
            owned = Process.Start(psi) ?? throw new InvalidOperationException("Vertcoin Core could not start.");
            var until = DateTime.UtcNow.AddSeconds(40);
            while (DateTime.UtcNow < until)
            {
                ct.ThrowIfCancellationRequested();
                if (owned.HasExited) throw new InvalidOperationException($"Vertcoin Core exited (code {owned.ExitCode}). Check debug.log in its data folder.");
                try { using var client = new CoreRpcClient(launched); await client.GetStatusAsync(ct); return; }
                catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or CoreRpcException or IOException or TaskCanceledException && !ct.IsCancellationRequested) { }
                await Task.Delay(500, ct);
            }
            throw new TimeoutException("Core is still starting. Its process has been left running; use Refresh to check progress or Stop Core for a graceful shutdown.");
        }
        finally { lifecycle.Release(); }
    }

    private bool MatchesOwned(CoreNodeSettings settings) => IsOwnedRunning && launched is not null
        && settings.RpcPort == launched.RpcPort && settings.Network == launched.Network
        && string.Equals(Path.GetFullPath(settings.DataDirectory), Path.GetFullPath(launched.DataDirectory), StringComparison.OrdinalIgnoreCase);
    private CoreRpcClient Client(CoreNodeSettings settings) => new(MatchesOwned(settings) ? launched! : settings);

    public async Task<CoreNodeStatus> GetStatusAsync(CoreNodeSettings settings, CancellationToken ct = default)
    { using var c = Client(settings); return (await c.GetStatusAsync(ct)) with { Owned = MatchesOwned(settings) }; }
    public async Task<IReadOnlyList<string>> ListWalletsAsync(CoreNodeSettings settings, CancellationToken ct = default)
    { using var c = Client(settings); return await c.ListWalletsAsync(ct); }
    public async Task<CoreWalletStatus> GetWalletAsync(CoreNodeSettings settings, string wallet, CancellationToken ct = default)
    { using var c = Client(settings); return await c.GetWalletAsync(wallet, ct); }
    public async Task<string> GetNewAddressAsync(CoreNodeSettings settings, string wallet, CancellationToken ct = default)
    { using var c = Client(settings); return await c.GetNewAddressAsync(wallet, ct); }
    public async Task<string> CreateWalletAsync(CoreNodeSettings settings, string name, string passphrase, CancellationToken ct = default)
    {
        if (!MatchesOwned(settings)) throw new InvalidOperationException("Create a wallet only in the Core process started by this app. Existing external wallets are managed in Vertcoin Core.");
        using var c = Client(settings); return await c.CreateWalletAsync(name, passphrase, ct);
    }

    public async Task StopOwnedAsync(CancellationToken ct = default)
    {
        await lifecycle.WaitAsync(ct);
        try
        {
            if (!IsOwnedRunning) { ReleaseExited(); return; }
            using var client = new CoreRpcClient(launched!);
            await client.StopAsync(ct);
            try { await owned!.WaitForExitAsync(ct).WaitAsync(TimeSpan.FromSeconds(45), ct); }
            catch (TimeoutException) { throw new TimeoutException("Core is still flushing its chain or wallet data. It was not force-closed. Wait and refresh its status."); }
            ReleaseExited();
        }
        finally { lifecycle.Release(); }
    }

    private void ReleaseExited()
    {
        if (IsOwnedRunning) return;
        owned?.Dispose(); owned = null;
        if (isolatedConfig is not null) try { File.Delete(isolatedConfig); } catch (IOException) { }
        if (launched is not null && launched.CookieFile.Length > 0) try { File.Delete(launched.CookieFile); } catch (IOException) { }
        isolatedConfig = null; launched = null;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        ReleaseExited();
        // Caller decides whether to await graceful StopOwnedAsync before disposal.
        owned?.Dispose(); owned = null;
    }
}
