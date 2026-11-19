using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VertcoinOcm;

internal static class CoreTests
{
    public static async Task RunAsync(Action<bool, string> check, string scratch)
    {
        bool Throws(Action action) { try { action(); return false; } catch { return true; } }
        async Task<bool> ThrowsAsync(Func<Task> action) { try { await action(); return false; } catch { return true; } }
        var dir = Path.Combine(scratch, "core-tests"); Directory.CreateDirectory(dir);
        var settings = new CoreNodeSettings { DataDirectory = dir };
        check(!settings.Listen && settings.StopWithApp && settings.PruneMiB == 2048, "Core defaults to outbound peers, bounded block storage and graceful stop");
        check(Throws(() => CoreRpcClient.ValidateEndpoint(new Uri("http://192.168.1.2:5888/")))
            && Throws(() => CoreRpcClient.ValidateEndpoint(new Uri("http://localhost.example:5888/")))
            && Throws(() => CoreRpcClient.ValidateEndpoint(new Uri("http://user:pass@127.0.0.1:5888/"))), "Core RPC rejects remote endpoints and URL credentials");
        check(CoreRpcClient.ValidateEndpoint(new Uri("http://127.0.0.1:5888/")).IsLoopback, "Core RPC accepts only a literal loopback endpoint");
        check(Throws(() => new CoreNodeSettings { DataDirectory = dir, RpcPort = 5889 }.Validate())
            && Throws(() => new CoreNodeSettings { DataDirectory = dir, PruneMiB = 549 }.Validate()), "Core rejects conflicting ports and undersized prune targets");
        check(Throws(() => CoreNodeSettings.ValidateWalletName("../outside"))
            && Throws(() => CoreNodeSettings.ValidateWalletName("C:\\outside")), "Core wallet names cannot traverse paths");
        var fakeExe = Path.Combine(dir, "vertcoind.exe"); File.WriteAllText(fakeExe, "not executed"); settings.ExecutablePath = fakeExe;
        var args = settings.LaunchArguments(Path.Combine(dir, "empty.conf"), Path.Combine(dir, "private.cookie"));
        check(args.Contains("-rpcbind=127.0.0.1") && args.Contains("-rpcallowip=127.0.0.1")
            && args.Contains("-nosettings") && args.Contains("-upnp=0") && args.Contains("-natpmp=0")
            && !args.Any(x => x.StartsWith("-rpcpassword=")), "Managed Core isolates config, cookie authentication and loopback RPC");
        settings.Network = "regtest"; settings.Listen = true;
        args = settings.LaunchArguments(Path.Combine(dir, "empty.conf"), Path.Combine(dir, "private.cookie"));
        check(args.Contains("-listen=0") && args.Contains("-connect=0") && args.Contains("-dnsseed=0")
            && args.Contains("-discover=0") && args.Contains("-fixedseeds=0"), "Core regtest cannot contact or accept network peers");
        settings.Network = "main"; settings.Listen = false;
        File.WriteAllText(Path.Combine(dir, ".cookie"), "__cookie__:test-only-cookie");
        var handler = new CoreFakeHandler();
        using (var rpc = new CoreRpcClient(settings, handler))
        {
            var status = await rpc.GetStatusAsync();
            check(status.Blocks == 200 && status.Headers == 250 && status.Connections == 4 && status.Inbound == 1
                && status.Outbound == 3 && status.Pruned && status.VerificationProgress == .8, "Core status reads real RPC fields without inventing synchronization progress");
            check(handler.Authorized && handler.AllLoopback, "Core cookie authentication stays on loopback");
            var wallet = await rpc.GetWalletAsync("My Wallet");
            check(wallet.Trusted == 1.25m && wallet.UntrustedPending == .1m && wallet.Encrypted && wallet.Locked
                && wallet.Descriptors && handler.LastWalletUri.EndsWith("/wallet/My%20Wallet"), "Core wallet balances preserve decimal precision and encode the wallet route");
            string address = await rpc.GetNewAddressAsync("My Wallet");
            check(address == "test-receiving-address" && handler.LastMethod == "getnewaddress", "Core receiving address uses only the receive RPC");
            await rpc.GetWalletAsync("");
            await rpc.GetNewAddressAsync("");
            check(handler.LastWalletUri.EndsWith("/wallet/", StringComparison.Ordinal), "Existing unnamed Core wallets use the explicit empty wallet route");
            string created = await rpc.CreateWalletAsync("FixtureWallet", "test-only-long-passphrase");
            check(created == "FixtureWallet" && handler.EncryptedWalletRequested, "Core wallet creation requests encryption and descriptor keys");
            check(await ThrowsAsync(() => rpc.CreateWalletAsync("FixtureWallet", "short")), "Core refuses an empty or short wallet encryption passphrase");
            check(await ThrowsAsync(() => rpc.CreateWalletAsync("", "test-only-long-passphrase")), "New Core wallets still require a named wallet");
            check(!handler.Methods.Any(x => x is "sendtoaddress" or "walletpassphrase" or "dumpprivkey"), "Core integration never invokes send, unlock or private-key export");
        }
        using (var rpc = new CoreRpcClient(settings, new CoreFakeHandler { Redirect = true }))
            check(await ThrowsAsync(() => rpc.GetStatusAsync()), "Core RPC redirects are rejected before credentials can be forwarded");
        File.WriteAllText(Path.Combine(dir, ".cookie"), "not-a-cookie");
        using (var rpc = new CoreRpcClient(settings, new CoreFakeHandler()))
            check(await ThrowsAsync(() => rpc.GetStatusAsync()), "Malformed Core authentication files are rejected");
        string managedCookie = "bitchblast-" + new string('a', 32) + ".cookie";
        File.WriteAllText(Path.Combine(dir, managedCookie), "__cookie__:owned-test");
        File.WriteAllText(Path.Combine(dir, "bitchblast-cookie-name.txt"), managedCookie);
        check(settings.CookiePath == Path.Combine(dir, managedCookie), "A left-running managed node can be read after an app restart");
        File.WriteAllText(Path.Combine(dir, "bitchblast-cookie-name.txt"), "../../outside.cookie");
        check(settings.CookiePath == Path.Combine(dir, ".cookie"), "Managed cookie lookup cannot escape the node data folder");
        using (var manager = new CoreNodeManager())
        {
            await manager.StopOwnedAsync();
            check(!manager.IsOwnedRunning && await ThrowsAsync(() => manager.CreateWalletAsync(settings, "Wallet", "test-only-long-passphrase")),
                "Unowned Core processes cannot be stopped or have wallets created by the manager");
        }
        settings.WalletName = "";
        settings.Validate();
        File.WriteAllText(Path.Combine(dir, "wallet.dat"), "fixture, not a real wallet");
        args = settings.LaunchArguments(Path.Combine(dir, "empty.conf"), Path.Combine(dir, "private.cookie"));
        check(args.Contains("-wallet=") && args.Contains("-walletdir=" + dir) && !Directory.Exists(Path.Combine(dir, "wallets")),
            "Managed Core preserves an existing unnamed wallet directory convention");
        var binding = CoreAddressBinding.Capture("fixture-address", "WalletOne", settings);
        check(binding.Matches("WalletOne", settings.Copy()), "Core receiving address remains bound to the same wallet and node");
        check(!binding.Matches("WalletTwo", settings), "Changing the selected wallet invalidates its previously displayed receiving address");
        var other = settings.Copy(); other.DataDirectory = Path.Combine(dir, "different-node");
        check(!binding.Matches("WalletOne", other), "Changing the Core data folder invalidates a previous receiving address");
        other = settings.Copy(); other.RpcPort++;
        check(!binding.Matches("WalletOne", other), "Changing the Core RPC port invalidates a previous receiving address");
        other = settings.Copy(); other.Network = "regtest";
        check(!binding.Matches("WalletOne", other), "Changing the Core network invalidates a previous receiving address");
    }

    private sealed class CoreFakeHandler : HttpMessageHandler
    {
        public bool Authorized, AllLoopback = true, EncryptedWalletRequested, Redirect;
        public string LastWalletUri = "", LastMethod = "";
        public readonly List<string> Methods = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Authorized = request.Headers.Authorization?.Parameter == Convert.ToBase64String(Encoding.UTF8.GetBytes("__cookie__:test-only-cookie"));
            AllLoopback &= request.RequestUri!.Host == "127.0.0.1";
            if (Redirect) return new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri("http://example.com/") } };
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            string method = body.RootElement.GetProperty("method").GetString()!;
            LastMethod = method; Methods.Add(method);
            if (request.RequestUri.AbsolutePath.StartsWith("/wallet/")) LastWalletUri = request.RequestUri.AbsoluteUri;
            object result = method switch
            {
                "getblockchaininfo" => new { chain = "main", blocks = 200, headers = 250, verificationprogress = .8, initialblockdownload = true, pruned = true, size_on_disk = 2048L },
                "getnetworkinfo" => new { connections = 4, connections_in = 1, connections_out = 3, version = 230200, subversion = "/Vertcoin:23.2/", networkactive = true },
                "getwalletinfo" => new { walletname = "My Wallet", private_keys_enabled = true, unlocked_until = 0, descriptors = true, scanning = false },
                "getbalances" => new { mine = new { trusted = 1.25m, untrusted_pending = .1m, immature = 0m } },
                "getnewaddress" => "test-receiving-address",
                "createwallet" => new { name = "FixtureWallet" },
                "listwallets" => new[] { "My Wallet" },
                _ => throw new InvalidOperationException("Unexpected fixture RPC method.")
            };
            if (method == "createwallet")
            {
                var p = body.RootElement.GetProperty("params");
                EncryptedWalletRequested = p.GetProperty("passphrase").GetString()!.Length >= 12 && p.GetProperty("descriptors").GetBoolean();
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { result, error = (object?)null, id = 1 })) };
        }
    }
}

internal static class CoreSmoke
{
    public static async Task RunAsync(string executable, string? preparedData)
    {
        var root = Path.Combine(Path.GetTempPath(), "BitchBlast-Core-Smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "regtest"));
        if (!string.IsNullOrWhiteSpace(preparedData))
        {
            using var input = File.OpenRead(preparedData);
            string hash = Convert.ToHexString(await SHA256.HashDataAsync(input));
            if (hash != "A55531E843CD56B010114AAF6325B0D529ECF88F8AD47639B6EDEDAFD721AA48") throw new InvalidDataException("The supplied offline Verthash data file does not match the canonical hash.");
            File.Copy(preparedData, Path.Combine(root, "regtest", "verthash.dat"));
        }
        var a = new TcpListener(IPAddress.Loopback, 0); var b = new TcpListener(IPAddress.Loopback, 0);
        a.Start(); b.Start(); int rpc = ((IPEndPoint)a.LocalEndpoint).Port, peer = ((IPEndPoint)b.LocalEndpoint).Port; a.Stop(); b.Stop();
        var settings = new CoreNodeSettings { ExecutablePath = executable, DataDirectory = root, RpcPort = rpc, PeerPort = peer, Network = "regtest", WalletName = "SmokeWallet", Listen = false };
        using var manager = new CoreNodeManager();
        Console.WriteLine("Isolated Core regtest data: " + root);
        try
        {
            await manager.StartAsync(settings);
            var status = await manager.GetStatusAsync(settings);
            if (status.Chain != "regtest" || status.Connections != 0 || !status.Owned) throw new Exception("Regtest isolation failed.");
            Console.WriteLine("PASS: official Core starts with isolated regtest, zero peers and authenticated local RPC");
            await manager.CreateWalletAsync(settings, settings.WalletName, "Offline-regtest-only-" + Guid.NewGuid().ToString("N"));
            var wallet = await manager.GetWalletAsync(settings, settings.WalletName);
            if (!wallet.Encrypted || !wallet.Locked || wallet.Trusted != 0 || !wallet.Descriptors) throw new Exception("Encrypted wallet creation failed.");
            Console.WriteLine("PASS: official Core creates a locked encrypted descriptor wallet with zero funds");
            var address = await manager.GetNewAddressAsync(settings, settings.WalletName);
            if (!address.StartsWith("rvtc1", StringComparison.Ordinal)) throw new Exception("Core returned an unexpected regtest receiving address prefix.");
            Console.WriteLine("PASS: locked Core wallet produces a receiving address without unlock or spending");
            using var observer = new CoreNodeManager();
            var observed = await observer.GetStatusAsync(settings);
            if (observed.Owned) throw new Exception("Observer incorrectly claimed process ownership.");
            await observer.StopOwnedAsync();
            if (!manager.IsOwnedRunning) throw new Exception("Observer stopped an unowned node.");
            Console.WriteLine("PASS: a second manager reads but does not stop the unowned node");
        }
        finally { await manager.StopOwnedAsync(); }
        if (manager.IsOwnedRunning) throw new Exception("Core did not stop gracefully.");
        Console.WriteLine("PASS: official Core flushes and exits through authenticated RPC stop");
        try
        {
            await manager.StartAsync(settings);
            var wallets = await manager.ListWalletsAsync(settings);
            var reopened = await manager.GetWalletAsync(settings, settings.WalletName);
            if (!wallets.Contains(settings.WalletName) || !reopened.Locked || !reopened.Encrypted)
                throw new Exception("The encrypted wallet did not reload after a managed restart.");
            Console.WriteLine("PASS: the same encrypted wallet reloads locked after a managed Core restart");
        }
        finally { await manager.StopOwnedAsync(); }
    }
}
