using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;

namespace VertcoinOcm;

public partial class MainWindow
{
    private readonly CoreNodeManager core = new();
    private readonly DispatcherTimer coreTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool coreBusy, coreRefreshing, fillingWallets;
    private CoreAddressBinding? coreAddress;
    private long coreEpoch;
    private TaskCompletionSource? coreOperationCompletion;
    private void InitializeCore()
    {
        settings.Core ??= new();
        if (string.IsNullOrWhiteSpace(settings.Core.ExecutablePath) && File.Exists(CoreSetup.DefaultExe)) settings.Core.ExecutablePath = CoreSetup.DefaultExe;
        FillCoreSettings();
        coreTimer.Tick += async (_, _) => { if (!closed && !closing && !coreBusy) await RefreshCoreAsync(false); };
        UpdateCoreButtons();
    }
    private void FillCoreSettings()
    {
        var c = settings.Core;
        CoreExeInput.Text = c.ExecutablePath; CoreDataInput.Text = c.DataDirectory;
        CorePruneInput.Text = c.PruneMiB.ToString(); CoreRpcInput.Text = c.RpcPort.ToString(); CorePeerInput.Text = c.PeerPort.ToString();
        CoreListenToggle.IsChecked = c.Listen; CoreStopWithAppToggle.IsChecked = c.StopWithApp;
        CoreWalletNameInput.Text = c.WalletName;
    }
    private CoreNodeSettings ReadCoreSettings()
    {
        int Number(TextBox input, string name) => int.TryParse(input.Text, out int value) ? value : throw new ArgumentException($"Enter a whole number for {name}.");
        var next = settings.Core.Copy();
        next.ExecutablePath = CoreExeInput.Text.Trim(); next.DataDirectory = CoreDataInput.Text.Trim();
        next.PruneMiB = Number(CorePruneInput, "block storage"); next.RpcPort = Number(CoreRpcInput, "RPC port"); next.PeerPort = Number(CorePeerInput, "peer port");
        next.Listen = CoreListenToggle.IsChecked == true; next.StopWithApp = CoreStopWithAppToggle.IsChecked == true;
        next.Validate(); return next;
    }
    private void SaveCoreSettings()
    {
        var next = ReadCoreSettings();
        if (core.IsOwnedRunning && (next.ExecutablePath != settings.Core.ExecutablePath || next.DataDirectory != settings.Core.DataDirectory ||
            next.RpcPort != settings.Core.RpcPort || next.PeerPort != settings.Core.PeerPort || next.PruneMiB != settings.Core.PruneMiB || next.Listen != settings.Core.Listen))
            throw new InvalidOperationException("Stop the managed node before changing its paths, ports or storage settings.");
        if (!CoreAddressBinding.Capture("", "", settings.Core).Matches("", next))
        {
            coreEpoch++; ClearCoreStatus("Core connection changed. Connect to refresh.");
            CoreWalletSelect.ItemsSource = null;
        }
        if (coreAddress is not null && !coreAddress.Matches(CoreWalletSelect.SelectedItem as string ?? "", next)) ClearCoreAddress();
        settings.Core = next; settings.Save();
    }
    private void UpdateCoreButtons()
    {
        CorePrepareButton.IsEnabled = !coreBusy && !core.IsOwnedRunning;
        CoreStartButton.IsEnabled = !coreBusy && !core.IsOwnedRunning;
        CoreStopButton.IsEnabled = !coreBusy && core.IsOwnedRunning;
        CoreConnectButton.IsEnabled = !coreBusy;
        CoreWalletSelect.IsEnabled = !coreBusy;
    }
    private async Task CoreAction(Func<Task> action)
    {
        if (coreBusy || closing) return;
        coreBusy = true; coreOperationCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously); UpdateCoreButtons();
        try { await action(); }
        catch (OperationCanceledException) { CoreActionText.Text = "The Core operation was cancelled or timed out. Core may still be finishing; use Connect / refresh to check."; }
        catch (Exception ex) { CoreActionText.Text = ex.Message; }
        finally { coreBusy = false; UpdateCoreButtons(); coreOperationCompletion.TrySetResult(); }
    }
    private void CoreClick(object sender, RoutedEventArgs e) => ShowPanel(CorePanel);
    private async void CoreSaveClick(object sender, RoutedEventArgs e) => await CoreAction(() =>
    {
        SaveCoreSettings(); CoreActionText.Text = "Node settings saved. Changes take effect at the next managed start."; return Task.CompletedTask;
    });
    private async void CorePrepareClick(object sender, RoutedEventArgs e) => await CoreAction(async () =>
    {
        if (core.IsOwnedRunning) throw new InvalidOperationException("Stop Core before updating its program files.");
        string executable = await CoreSetup.InstallAsync(new Progress<string>(message => CoreActionText.Text = message), lifetime.Token);
        CoreExeInput.Text = executable; SaveCoreSettings();
    });
    private async void CoreStartClick(object sender, RoutedEventArgs e) => await CoreAction(async () =>
    {
        SaveCoreSettings(); CoreActionText.Text = "Starting Core. First sync uses network, disk and CPU; mining remains under your control.";
        coreEpoch++;
        await CoreSetup.PrepareVerthashAsync(settings.Core, settings.DataPath, new Progress<string>(message => CoreActionText.Text = message), lifetime.Token);
        coreTimer.Start();
        await core.StartAsync(settings.Core, lifetime.Token);
        coreTimer.Start(); await RefreshCoreAsync(true); await RefreshCoreWalletsAsync();
        CoreActionText.Text = "Managed node started. Its private RPC connection stays on this computer.";
    });
    private async void CoreConnectClick(object sender, RoutedEventArgs e) => await CoreAction(async () =>
    {
        SaveCoreSettings(); coreTimer.Start(); await RefreshCoreAsync(true); await RefreshCoreWalletsAsync();
        CoreActionText.Text = core.IsOwnedRunning ? "Connected to the managed node." : "Connected to an existing local Core node. Bitch Blast will not stop a process it did not start.";
    });
    private async void CoreStopClick(object sender, RoutedEventArgs e) => await CoreAction(async () =>
    {
        CoreActionText.Text = "Asking Core to save its state and stop safely…";
        coreEpoch++;
        await core.StopOwnedAsync(); coreTimer.Stop(); ClearCoreStatus("Managed node stopped.");
        CoreActionText.Text = "Core stopped gracefully. Its data and wallets are preserved.";
    });
    private void ClearCoreStatus(string state)
    {
        ClearCoreAddress();
        CoreStatusText.Text = state; CoreChainText.Text = "No active Core connection.";
        CoreBlocksText.Text = CorePeersText.Text = CoreDiskText.Text = "—"; CoreSyncProgress.Value = 0;
        CoreWalletText.Text = "Wallet balance unavailable until Core reconnects.";
        nodeHudSummary = state; UpdateGalleryHud();
    }
    private async Task RefreshCoreAsync(bool throwOnFailure)
    {
        if (coreRefreshing || closing) return;
        coreRefreshing = true;
        long epoch = coreEpoch;
        var connection = settings.Core.Copy();
        try
        {
            var status = await core.GetStatusAsync(connection, lifetime.Token);
            if (epoch != coreEpoch || closing) return;
            double percent = Math.Clamp(status.VerificationProgress * 100, 0, 100);
            string sync = status.InitialBlockDownload ? $"Syncing · {percent:N2}% estimated" : "Initial sync complete";
            CoreStatusText.Text = sync;
            CoreSyncProgress.Value = percent;
            CoreChainText.Text = $"{status.Subversion} · {status.Chain} network · {(status.Pruned ? "pruned" : "archival")} · {(status.Owned ? "managed by Bitch Blast" : "existing local node")}" + (status.NetworkActive ? "" : " · networking disabled");
            CoreBlocksText.Text = $"{status.Blocks:N0} / {status.Headers:N0}";
            CorePeersText.Text = $"{status.Connections} · {status.Inbound} in / {status.Outbound} out";
            CoreDiskText.Text = $"{status.DiskBytes / 1073741824d:N2} GiB";
            nodeHudSummary = $"{status.Chain} · {(status.InitialBlockDownload ? $"sync {percent:N1}%" : "initial sync complete")} · {status.Connections} peers";
            UpdateGalleryHud(); UpdateCoreButtons();
        }
        catch (Exception ex)
        {
            if (!closing && epoch == coreEpoch) { ClearCoreStatus("Core unavailable or still starting."); CoreChainText.Text = ex is OperationCanceledException ? "Local Core did not answer in time." : ex.Message; UpdateCoreButtons(); }
            if (throwOnFailure) throw;
        }
        finally { coreRefreshing = false; }
    }
    private async Task RefreshCoreWalletsAsync(string? desiredName = null)
    {
        var wallets = await core.ListWalletsAsync(settings.Core, lifetime.Token);
        string wanted = desiredName ?? CoreWalletSelect.SelectedItem as string ?? settings.Core.WalletName;
        fillingWallets = true;
        try { CoreWalletSelect.ItemsSource = wallets; CoreWalletSelect.SelectedItem = wallets.Contains(wanted) ? wanted : wallets.FirstOrDefault(); }
        finally { fillingWallets = false; }
        await RefreshCoreWalletAsync();
    }
    private async Task RefreshCoreWalletAsync()
    {
        if (CoreWalletSelect.SelectedItem is not string wallet) { CoreWalletText.Text = "No loaded wallet. Create one below or use Core’s wallet window."; return; }
        CoreWalletText.Text = "Refreshing the selected Core wallet…";
        var identity = CoreAddressBinding.Capture("", wallet, settings.Core);
        var value = await core.GetWalletAsync(settings.Core, wallet, lifetime.Token);
        if (CoreWalletSelect.SelectedItem is not string selected || !identity.Matches(selected, settings.Core)) return;
        CoreWalletText.Text = $"{value.Trusted:N8} VTC trusted · {value.UntrustedPending:N8} pending · {value.Immature:N8} immature\n{(value.Encrypted ? value.Locked ? "Encrypted · locked" : "Encrypted · unlocked in Core" : "Not encrypted")} · {(value.PrivateKeysEnabled ? "keys held by Core" : "watch-only")} · {(value.Scanning ? "wallet scan in progress" : "refresh for current balance")}";
        settings.Core.WalletName = wallet; settings.Save();
    }
    private async void CoreWalletRefreshClick(object sender, RoutedEventArgs e) => await CoreAction(() => RefreshCoreWalletsAsync());
    private async void CoreWalletSelected(object sender, SelectionChangedEventArgs e)
    {
        ClearCoreAddress();
        CoreWalletText.Text = "Wallet selection changed. Refreshing its balance…";
        if (!fillingWallets) await CoreAction(RefreshCoreWalletAsync);
    }
    private async void CoreReceiveClick(object sender, RoutedEventArgs e) => await CoreAction(async () =>
    {
        if (CoreWalletSelect.SelectedItem is not string wallet) throw new InvalidOperationException("Choose a loaded Core wallet first.");
        var identity = CoreAddressBinding.Capture("", wallet, settings.Core);
        string address = await core.GetNewAddressAsync(settings.Core, wallet, lifetime.Token);
        if (CoreWalletSelect.SelectedItem is not string selected || !identity.Matches(selected, settings.Core))
            throw new InvalidOperationException("The selected wallet changed. Create a receiving address again for the current wallet.");
        coreAddress = identity with { Address = address }; CoreReceiveAddress.Text = address;
        CoreActionText.Text = "New receiving address created in Core. Back up the wallet before funding it. Choose Use this address for mining to change payouts.";
    });
    private async void CoreCreateWalletClick(object sender, RoutedEventArgs e) => await CoreAction(async () =>
    {
        string passphrase = CorePassphrase.Password;
        if (passphrase != CorePassphraseConfirm.Password) throw new ArgumentException("The two passphrases do not match.");
        string name = CoreWalletNameInput.Text.Trim();
        try
        {
            await core.CreateWalletAsync(settings.Core, name, passphrase, lifetime.Token);
            settings.Core.WalletName = name; settings.Save(); await RefreshCoreWalletsAsync(name);
            CoreActionText.Text = "Encrypted wallet created. Open Core’s wallet window and make a backup before sending any funds to it. Keep your passphrase safe.";
        }
        finally { CorePassphrase.Clear(); CorePassphraseConfirm.Clear(); }
    });
    private async void CoreUseAddressClick(object sender, RoutedEventArgs e) => await CoreAction(() =>
    {
        if (controller.Enabled) throw new InvalidOperationException("Stop mining before changing its payout destination.");
        string address = BoundCoreAddress();
        if (!AddressValidator.Valid(address)) throw new InvalidOperationException("Create a valid Vertcoin mainnet receiving address first. Test-network addresses cannot receive mainnet payouts.");
        settings.Address = address; AddressInput.Text = address; settings.Save(); UpdateWallet();
        CoreActionText.Text = "Mining payout address updated to the selected Core receiving address. Existing pool balances remain under their original address.";
        return Task.CompletedTask;
    });
    private async void CoreCopyAddressClick(object sender, RoutedEventArgs e) => await CoreAction(() =>
    {
        Clipboard.SetText(BoundCoreAddress()); CoreActionText.Text = "Receiving address copied."; return Task.CompletedTask;
    });
    private void ClearCoreAddress() { coreAddress = null; CoreReceiveAddress.Clear(); }
    private string BoundCoreAddress()
    {
        if (coreAddress is null || CoreWalletSelect.SelectedItem is not string selected || !coreAddress.Matches(selected, settings.Core) || CoreReceiveAddress.Text != coreAddress.Address)
            throw new InvalidOperationException("Create a receiving address for the currently selected Core wallet first.");
        return coreAddress.Address;
    }
    private async void CoreOpenWalletClick(object sender, RoutedEventArgs e) => await CoreAction(async () =>
    {
        SaveCoreSettings();
        string directory = Path.GetDirectoryName(Path.GetFullPath(settings.Core.ExecutablePath))!;
        string gui = Path.Combine(directory, "vertcoin-qt.exe");
        if (!File.Exists(gui)) throw new FileNotFoundException("Prepare the official Core package first, or select a Core installation containing vertcoin-qt.exe.");
        if (!core.IsOwnedRunning)
        {
            bool existing = false;
            try { await core.GetStatusAsync(settings.Core, lifetime.Token); existing = true; } catch { }
            if (existing) throw new InvalidOperationException("An existing Core instance is using this data folder. Open its wallet window directly, or stop it there before opening a managed wallet window.");
        }
        coreEpoch++;
        if (core.IsOwnedRunning) { CoreActionText.Text = "Saving the managed node before opening Core’s wallet window…"; await core.StopOwnedAsync(); }
        var options = settings.Core.Copy(); options.ExecutablePath = gui;
        await CoreSetup.PrepareVerthashAsync(options, settings.DataPath, new Progress<string>(message => CoreActionText.Text = message), lifetime.Token);
        coreTimer.Start();
        await core.StartAsync(options, lifetime.Token); coreTimer.Start(); await RefreshCoreAsync(true); await RefreshCoreWalletsAsync();
        CoreActionText.Text = "Core’s official wallet window is open. Use it for backups, encryption, recovery and sending coins.";
    });
    private void CoreBrowseClick(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Filter = "Vertcoin Core programs|vertcoind.exe;vertcoin-qt.exe" };
        if (picker.ShowDialog(this) == true) CoreExeInput.Text = picker.FileName;
    }
    private void CoreDataBrowseClick(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog { Title = "Choose Core’s data folder" };
        if (picker.ShowDialog(this) == true) CoreDataInput.Text = picker.FolderName;
    }
    private void CoreNetworkHintsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var lines = new List<string>();
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
            {
                var properties = adapter.GetIPProperties();
                var gateways = properties.GatewayAddresses.Where(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(System.Net.IPAddress.Any)).Select(g => g.Address.ToString()).ToArray();
                if (gateways.Length == 0) continue;
                var addresses = properties.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork).Select(a => a.Address.ToString());
                lines.Add($"{adapter.Name}: {string.Join(", ", addresses)}\nRouter gateway: {string.Join(", ", gateways)}");
            }
            CoreNetworkHints.Text = lines.Count > 0 ? string.Join("\n\n", lines) + "\n\nUse your home Ethernet/Wi-Fi adapter, not a VPN. These are local addresses, not an inbound reachability test." : "No active IPv4 gateway found. Check Windows network details for your Ethernet or Wi-Fi connection.";
        }
        catch (NetworkInformationException) { CoreNetworkHints.Text = "Network details are unavailable. Open Windows network settings to find the local address and gateway."; }
    }
    private async void CoreGuideClick(object sender, RoutedEventArgs e) => await Safe(() =>
    {
        string guide = Path.Combine(AppContext.BaseDirectory, "docs", "CORE-NODE.md");
        Open(File.Exists(guide) ? guide : "https://github.com/jonacitrusthyng/vertcoin-bitch-blast/blob/main/docs/CORE-NODE.md"); return Task.CompletedTask;
    });
    private async void CoreReleaseClick(object sender, RoutedEventArgs e) => await Safe(() => { Open(CoreSetup.ReleaseUrl); return Task.CompletedTask; });
    private async Task CloseCoreAsync()
    {
        if (coreOperationCompletion is { } pending) await pending.Task;
        if (settings.Core.StopWithApp && core.IsOwnedRunning)
        {
            FooterStatus.Text = "Saving Core state and stopping the managed node safely…";
            try { await core.StopOwnedAsync(); }
            catch (Exception ex) { MessageBox.Show(this, "Core is still finishing its shutdown and has been left running safely.\n\n" + ex.Message, "Vertcoin Core shutdown", MessageBoxButton.OK, MessageBoxImage.Information); }
        }
        core.Dispose();
    }
}
