using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
namespace VertcoinOcm;
public partial class MainWindow : Window
{
    private Settings settings = Settings.Load();
    private readonly MiningController controller = new();
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? setup;
    private bool ready, closing, closed;
    public MainWindow()
    {
        InitializeComponent();
        PoolSelect.ItemsSource = Pool.Catalog;
        PoolSelect.SelectedItem = settings.Pool;
        if (string.IsNullOrWhiteSpace(settings.MinerPath) && File.Exists(MinerSetup.DefaultExe)) settings.MinerPath=MinerSetup.DefaultExe;
        if (string.IsNullOrWhiteSpace(settings.DataPath) && File.Exists(MinerSetup.DefaultData)) settings.DataPath=MinerSetup.DefaultData;
        FillSettings(); UpdateWallet(); UpdatePool();
        controller.Changed += status => { if(Dispatcher.CheckAccess()) UpdateStatus(status); else Dispatcher.BeginInvoke(() => UpdateStatus(status)); };
        ready = true;
        InitializeAudio();
        InitializeCore();
        Loaded += async (_,_) => { Comment("welcome","Welcome to Bitch Blast. The walnut has left the trailer.",true); if(File.Exists(settings.MinerPath)) await Safe(async()=>await ScanGpu()); };
    }
    private void FillSettings()
    {
        AddressInput.Text=settings.Address; MinerInput.Text=settings.MinerPath; DataInput.Text=settings.DataPath;
        AutoToggle.IsChecked=settings.AutoAi; IntensityInput.Text=settings.Occupancy.ToString(); TemperatureInput.Text=settings.Temperature.ToString(); IdleInput.Text=settings.IdleSeconds.ToString();
        ProcessesInput.Text=settings.PauseProcesses; CustomUrlInput.Text=settings.CustomUrl; CustomPasswordInput.Text=settings.CustomPassword; WorkerInput.Text=settings.WorkerName;
        AiSummary.Text=settings.AutoAi ? "Pause for local AI. Resume when it’s idle." : "Manual mode · mining keeps the selected GPU.";
    }
    private void UpdateWallet()
    {
        bool has=AddressValidator.Valid(settings.Address);
        WalletSummary.Text=has ? settings.Address : "Add your receiving address to get started.";
        WalletSummary.FontSize=has ? 13 : 15;
        WalletButton.Content=has ? "▣  Manage wallet" : "＋  Set payout wallet";
        FooterStatus.Text=has ? "✓  Payout wallet saved · rewards go directly from the pool to you." : "✓  Set your payout address to get started.";
    }
    private void UpdatePool()
    {
        PoolEndpoint.Text=settings.PoolUrl=="" ? "Set your custom endpoint in Settings." : settings.PoolUrl;
        PoolFee.Text=settings.Pool.Fee; PoolStatus.Text="Connection not checked for this selection.";
    }
    private void ShowPanel(FrameworkElement panel)
    {
        foreach(var p in new FrameworkElement[]{Dashboard,WalletPanel,SettingsPanel,ActivityPanel,HelpPanel,JukeboxPanel,CorePanel}) p.Visibility=p==panel ? Visibility.Visible:Visibility.Collapsed;
        HeroPanel.Visibility=panel==Dashboard?Visibility.Visible:Visibility.Collapsed; MainScroll.ScrollToTop();
    }
    private void DashboardClick(object s,RoutedEventArgs e)=>ShowPanel(Dashboard);
    private void WalletClick(object s,RoutedEventArgs e){ShowPanel(WalletPanel);Comment("wallet","Your coins, your damn wallet. Paste a public VTC address.");}
    private void SettingsClick(object s,RoutedEventArgs e){ShowPanel(SettingsPanel);Comment("settings","Under the hood. Please leave the trailer roof attached.");}
    private void ActivityClick(object s,RoutedEventArgs e){ShowPanel(ActivityPanel);Comment("receipts","The receipts. Actual miner output. No imaginary yacht money.");}
    private void HelpClick(object s,RoutedEventArgs e)=>ShowPanel(HelpPanel);
    private void CloseClick(object s,RoutedEventArgs e)=>Close();
    private void MinimizeClick(object s,RoutedEventArgs e)=>WindowState=WindowState.Minimized;
    private void MaximizeClick(object s,RoutedEventArgs e)=>WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;
    private async Task Safe(Func<Task> action)
    {
        try { await action(); }
        catch(OperationCanceledException) { FooterStatus.Text="Operation cancelled or timed out. You can try again."; }
        catch(Exception ex) { FooterStatus.Text=ex.Message; MessageBox.Show(this,ex.Message,"Vertcoin Bitch Blast! Edition needs attention",MessageBoxButton.OK,MessageBoxImage.Information); }
    }
    private async void StartClick(object s,RoutedEventArgs e) => await Safe(async()=>
    {
        StartButton.IsEnabled=false;
        try
        {
            if(controller.Enabled) { await controller.StopAsync(); SetRunning(false); PlayEffect("stop"); Comment("stop","Mining stopped. The walnut is back in its little chair.",true); return; }
            if(!AddressValidator.Valid(settings.Address)) { ShowPanel(WalletPanel); AddressInput.Focus(); FooterStatus.Text="Add your public Vertcoin receiving address first."; return; }
            try { settings.Validate(); } catch { ShowPanel(SettingsPanel); throw; }
            controller.Start(settings); SetRunning(true); PlayEffect("blast"); Comment("start","Blast requested. The GPU still has to do the paperwork.",true); StartButton.Content="■  Stop mining";
        }
        finally { StartButton.IsEnabled=true; }
    });
    private void SetRunning(bool running)
    {
        PoolSelect.IsEnabled=!running; SettingsPanel.IsEnabled=!running; WalletPanel.IsEnabled=!running;
        StartButton.Content=running ? "■  Stop mining" : "▶  Blast off · start mining";
    }
    private void UpdateStatus(MiningStatus s)
    {
        if(closed) return;
        OnMiningAudio(s);
        latestMiningStatus=s;UpdateGalleryHud();
        StatusBadge.Text=(s.Active ? "●  " : "○  ")+s.State;
        MiningDetail.Text=s.Detail; HashrateText.Text=s.Hashrate>0 ? $"{s.Hashrate:N2} kH/s" : "— kH/s";
        SharesText.Text=$"{s.Accepted} accepted"; RejectedText.Text=s.Total>0 ? $"{s.Total-s.Accepted} rejected · {s.Total} submitted" : "No shares submitted yet";
        AiSummary.Text=settings.AutoAi ? $"AI: {s.Ai} · resumes after {settings.IdleSeconds}s idle" : "Manual mode · mining keeps the selected GPU.";
        if(s.Log!="") { LogBox.Text=s.Log; LogBox.ScrollToEnd(); }
        if(s.State.StartsWith("Mining stopped") || s.State=="Controller already running") SetRunning(false);
    }
    private async void AutoChanged(object s,RoutedEventArgs e)
    {
        if(!ready) return;
        await Safe(async()=>
        {
            AutoToggle.IsEnabled=false; bool restart=controller.Enabled;
            try { if(restart) await controller.StopAsync(); settings.AutoAi=AutoToggle.IsChecked==true; settings.Save(); Comment(settings.AutoAi?"auto_on":"auto_off",settings.AutoAi?"AI gets first dibs. Mining can wait its damn turn.":"Manual mode. You are the GPU traffic controller.",true); AiSummary.Text=settings.AutoAi?"Pause for local AI. Resume when it’s idle.":"Manual mode · mining keeps the selected GPU."; if(restart){controller.Start(settings);SetRunning(true);} }
            finally { AutoToggle.IsEnabled=true; }
        });
    }
    private void PoolChanged(object s,SelectionChangedEventArgs e)
    {
        if(!ready || PoolSelect.SelectedItem is not Pool pool) return;
        settings.PoolId=pool.Id; settings.Save(); UpdatePool(); PlayEffect("click"); Comment("pool","New pool selected. Same walnut. Different water.");
        if(pool.Id=="custom") ShowPanel(SettingsPanel);
    }
    private async void CheckPoolClick(object s,RoutedEventArgs e) => await Safe(async()=>
    {
        var pool=settings.PoolUrl; var b=(Button)s; b.IsEnabled=false; PoolStatus.Text="Checking Stratum connection…";
        try { var result=await MinerSetup.CheckPoolAsync(pool,lifetime.Token); if(settings.PoolUrl==pool) PoolStatus.Text=result; }
        catch(Exception ex) { if(settings.PoolUrl==pool)PoolStatus.Text="Could not connect: "+(ex is OperationCanceledException?"request timed out":ex.Message); }
        finally { b.IsEnabled=true; }
    });
    private async void SaveWalletClick(object s,RoutedEventArgs e)=>await Safe(()=>
    {
        string address=AddressInput.Text.Trim(); if(!AddressValidator.Valid(address))throw new ArgumentException("That is not a valid mainnet Vertcoin address. Copy a receiving address from your Vertcoin wallet and try again.");
        settings.Address=address; settings.Save(); UpdateWallet(); ShowPanel(Dashboard); PlayEffect("save"); Comment("saved","Saved. The nut fund has a forwarding address.",true); return Task.CompletedTask;
    });
    private async void CopyAddressClick(object s,RoutedEventArgs e)=>await Safe(()=>
    {
        if(!AddressValidator.Valid(settings.Address)){ShowPanel(WalletPanel);return Task.CompletedTask;}
        Clipboard.SetText(settings.Address);FooterStatus.Text="Receiving address copied.";return Task.CompletedTask;
    });
    private static void Open(string url)=>Process.Start(new ProcessStartInfo(url){UseShellExecute=true});
    private async void GetWalletClick(object s,RoutedEventArgs e)=>await Safe(()=>{Open("https://vertcoin.org/");return Task.CompletedTask;});
    private async void MinerSourceClick(object s,RoutedEventArgs e)=>await Safe(()=>{Open("https://github.com/CryptoGraphics/VerthashMiner");return Task.CompletedTask;});
    private async void OpenWalletClick(object s,RoutedEventArgs e)=>await Safe(()=>{Open("vertcoin:");return Task.CompletedTask;});
    private async void PoolDashboardClick(object s,RoutedEventArgs e)=>await Safe(()=>
    {
        var address=Uri.EscapeDataString(settings.Address);
        string url=settings.PoolId switch { "zpool" when AddressValidator.Valid(settings.Address)=>"https://zpool.ca/wallet/"+address,"suprnova" when AddressValidator.Valid(settings.Address)=>"https://vtc.suprnova.cc/index.php?page=anondashboard&user="+address,_=>settings.Pool.Website};
        Open(url);return Task.CompletedTask;
    });
    private async void PrepareClick(object s,RoutedEventArgs e)=>await Safe(async()=>
    {
        if(setup!=null)return;
        setup=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); PrepareButton.IsEnabled=false; CancelSetupButton.IsEnabled=true; StartButton.IsEnabled=false;
        var progress=new Progress<string>(text=>{SetupStatus.Text=text;FooterStatus.Text=text;});
        try
        {
            string exe=File.Exists(MinerInput.Text)?Path.GetFullPath(MinerInput.Text):await MinerSetup.InstallAsync(progress,setup.Token);
            MinerInput.Text=exe;settings.MinerPath=exe; settings.Save();
            string data=string.IsNullOrWhiteSpace(DataInput.Text)?MinerSetup.DefaultData:Path.GetFullPath(DataInput.Text);
            if(!File.Exists(data) || new FileInfo(data).Length!=MinerSetup.DataBytes) await MinerSetup.GenerateDataAsync(exe,data,progress,setup.Token);
            DataInput.Text=data;settings.DataPath=data;settings.Save();await ScanGpu();
            SetupStatus.Text="Miner and data are ready. Choose a GPU, save settings, then start mining.";
            FooterStatus.Text="✓  Your miner is ready.";
        }
        finally { setup.Dispose();setup=null;PrepareButton.IsEnabled=true;CancelSetupButton.IsEnabled=false;StartButton.IsEnabled=true; }
    });
    private void CancelSetupClick(object s,RoutedEventArgs e)=>setup?.Cancel();
    private async Task ScanGpu()
    {
        string key=(GpuSelect.SelectedItem as Gpu)?.Key??settings.GpuKey;
        string exe=MinerInput.Text;
        if(!File.Exists(exe))throw new FileNotFoundException("Prepare the miner or select VerthashMiner.exe first.");
        var devices=await MinerSetup.ScanAsync(exe,lifetime.Token);GpuSelect.ItemsSource=devices;
        GpuSelect.SelectedItem=devices.FirstOrDefault(g=>g.Key==key); // Never silently choose a different GPU.
        SetupStatus.Text=devices.Count>0?$"Found {devices.Count} GPU devices. Choose one and save settings.":"No compatible GPUs found. Check your GPU driver.";
    }
    private async void ScanClick(object s,RoutedEventArgs e)=>await Safe(ScanGpu);
    private async void TestGpuClick(object s,RoutedEventArgs e)=>await Safe(async()=>
    {
        if(setup!=null)throw new InvalidOperationException("Wait for setup to finish first.");
        if(GpuSelect.SelectedItem is not Gpu gpu)throw new ArgumentException("Scan and select a GPU first.");
        if(await MiningController.ReadAiAsync(settings,lifetime.Token)!=IdleGpu.AiState.Idle)throw new InvalidOperationException("AI is busy or its status is unavailable. Wait until it is idle before testing the GPU.");
        setup=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var button=(Button)s;button.IsEnabled=false;PrepareButton.IsEnabled=false;StartButton.IsEnabled=false;CancelSetupButton.IsEnabled=true;
        try { SetupStatus.Text="Testing the selected GPU offline · up to 35 seconds · no pool connection or wallet needed…";
            SetupStatus.Text=await MinerSetup.BenchmarkAsync(MinerInput.Text,DataInput.Text,gpu,settings.Occupancy,settings.Temperature,setup.Token); }
        finally { setup.Dispose();setup=null;button.IsEnabled=true;PrepareButton.IsEnabled=true;StartButton.IsEnabled=true;CancelSetupButton.IsEnabled=false; }
    });
    private void BrowseMinerClick(object s,RoutedEventArgs e)
    {
        var dialog=new OpenFileDialog{Filter="VerthashMiner executable|VerthashMiner.exe|Executable|*.exe"};if(dialog.ShowDialog(this)==true)MinerInput.Text=dialog.FileName;
    }
    private void BrowseDataClick(object s,RoutedEventArgs e)
    {
        var dialog=new OpenFileDialog{Filter="Verthash data|*.dat"};if(dialog.ShowDialog(this)==true)DataInput.Text=dialog.FileName;
    }
    private async void SaveSettingsClick(object s,RoutedEventArgs e)=>await Safe(()=>
    {
        int Number(TextBox box,int min,int max,string label){if(!int.TryParse(box.Text,out int n)||n<min||n>max)throw new ArgumentException($"{label} must be between {min} and {max}.");return n;}
        var next=settings.Copy();next.MinerPath=MinerInput.Text.Trim();next.DataPath=DataInput.Text.Trim();next.GpuKey=(GpuSelect.SelectedItem as Gpu)?.Key??"";
        if(next.GpuKey=="")throw new ArgumentException("Scan and choose a GPU before saving.");
        next.Occupancy=Number(IntensityInput,1,100,"Intensity");next.Temperature=Number(TemperatureInput,50,90,"Temperature limit");next.IdleSeconds=Number(IdleInput,5,3600,"Idle delay");
        next.PauseProcesses=ProcessesInput.Text.Trim();next.CustomUrl=CustomUrlInput.Text.Trim();next.CustomPassword=CustomPasswordInput.Text;next.WorkerName=WorkerInput.Text.Trim();
        if(next.PoolId=="custom")Settings.ValidatePool(next.CustomUrl);
        next.Save();settings=next;UpdatePool();PlayEffect("save");Comment("settings_saved","Settings saved. Responsible chaos. My favorite kind.",true);FooterStatus.Text="✓  Settings saved. Ready when you are.";ShowPanel(Dashboard);return Task.CompletedTask;
    });
    private async void LogsClick(object s,RoutedEventArgs e)=>await Safe(()=>{Directory.CreateDirectory(Settings.DataDir);Open(File.Exists(settings.MinerPath)?Path.GetDirectoryName(settings.MinerPath)!:Settings.DataDir);return Task.CompletedTask;});
    private async void CheckAiClick(object s,RoutedEventArgs e)=>await Safe(async()=>{AiCheckText.Text="Checking local AI…";var ai=await MiningController.ReadAiAsync(settings,lifetime.Token);AiCheckText.Text=$"Local AI: {ai}. No mining was started by this check.";});
    private async void OnClosing(object? sender,CancelEventArgs e)
    {
        if(closed)return;e.Cancel=true;if(closing)return;closing=true;
        gallery?.Close(); coreTimer.Stop();
        audioTimer.Stop(); audio?.Dispose(); SaveAudio();
        lifetime.Cancel();setup?.Cancel();
        // Defer completion until the original Closing event has returned, even when idle.
        await System.Windows.Threading.Dispatcher.Yield();
        await controller.DisposeAsync();
        await CloseCoreAsync();
        closed=true;lifetime.Dispose();Close();
    }
}

