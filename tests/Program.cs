using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VertcoinOcm;
using IdleGpu;

if(args.Length>0 && args[0]=="--url")
{
    File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"fixture-pid.txt"),Environment.ProcessId.ToString());
    File.WriteAllText(Path.Combine(Environment.CurrentDirectory,DateTime.UtcNow.ToString("yyyy-MM-dd HH-mm-ss")+".txt"),"INFO cl_device(0): temp:60C, hashrate: 125.50 kH/s\nINFO accepted: 2/3 (66.67%), total hashrate: 125.50 kH/s\n");
    await Task.Delay(Timeout.Infinite);return;
}
if(args.Length>0 && args[0]=="--fixture") { File.WriteAllText(args[1],JsonSerializer.Serialize(new {Pid=Environment.ProcessId,Args=args.Skip(2).ToArray()})); await Task.Delay(Timeout.Infinite); return; }
if(args.Length>0 && args[0]=="--owner") { using var w=new MinerWorker(Environment.ProcessPath!,"--fixture",args[1]);w.Start();await Task.Delay(Timeout.Infinite);return; }
if(args.Length>0 && args[0]=="--data") { await MinerSetup.GenerateDataAsync(args[1],args[2],new Progress<string>(Console.WriteLine),CancellationToken.None);return; }
if(args.Length>0 && args[0]=="--ai") { Console.WriteLine(await MiningController.ReadAiAsync(new Settings(),CancellationToken.None));return; }
if(args.Length>0 && args[0]=="--install") { Console.WriteLine(await MinerSetup.InstallAsync(new Progress<string>(Console.WriteLine),CancellationToken.None));return; }
if(args.Length>0 && args[0]=="--benchmark") { Console.WriteLine(await MinerSetup.BenchmarkAsync(args[1],args[2],new Gpu(args[3],int.Parse(args[4]),args[5]),75,75,CancellationToken.None));return; }
if(args.Length>0 && args[0]=="--pools") { foreach(var p in Pool.Catalog.Where(p=>p.Id!="custom")){try{Console.WriteLine(p.Name+": "+await MinerSetup.CheckPoolAsync(p.Url,CancellationToken.None));}catch(Exception e){Console.WriteLine(p.Name+": FAILED "+e.Message);}}return; }

if(args.Length>0 && args[0]=="--core-smoke") { await CoreSmoke.RunAsync(args[1],args.Length>2?args[2]:null);return; }
int passed=0;
void Check(bool value,string label) { if(!value)throw new Exception("FAIL: "+label); Console.WriteLine("PASS: "+label);passed++; }
bool Throws(Action action) {try{action();return false;}catch{return true;}}

// Real address from VerthashMiner usage examples, plus independent BIP-173 invalid vectors.
Check(AddressValidator.Valid("vtc1q49ed04t8vqclja67awkjjgxdre5n0exzumlm62"),"mainnet bech32 address");
Check(!AddressValidator.Valid("vtc1q49ed04t8vqclja67awkjjgxdre5n0exzumlm63"),"checksum typo rejected");
Check(!AddressValidator.Valid("VTC1q49ed04t8vqclja67awkjjgxdre5n0exzumlm62"),"mixed case rejected");
Check(!AddressValidator.Valid("bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t4"),"Bitcoin address rejected");
Check(!AddressValidator.Valid(""),"empty wallet rejected");
Check(Throws(()=>Settings.ValidatePool("stratum+tcp://host:1234 --evil")),"malformed endpoint rejected");
Check(Throws(()=>Settings.ValidatePool("https://example.com:443/")),"non-Stratum endpoint rejected");
Check(Throws(()=>Settings.ValidatePool("stratum+tcp://user:secret@host:1234")),"endpoint credentials rejected");
Check(Settings.ValidatePool("stratum+tcp://localhost:1234").Port==1234,"valid custom endpoint");
var gpus=Gpu.Parse("OpenCL devices:\n Index: 1. Name: Intel(R) Arc(TM) A770 Graphics\nCUDA devices:\n Index: 0. Name: NVIDIA GeForce RTX 3050 Ti Laptop GPU. pcieId: 01:00:0");
Check(gpus.Count==2 && gpus[0].Key=="cl:1" && gpus[1].Name=="NVIDIA GeForce RTX 3050 Ti Laptop GPU","GPU backend and index mapping");
var policy=new IdlePolicy();
Check(!policy.ShouldMine(true,AiState.Idle,false,TimeSpan.Zero,30),"auto waits before first start");
Check(policy.ShouldMine(true,AiState.Idle,false,TimeSpan.FromSeconds(30),30),"auto starts after idle interval");
Check(!policy.ShouldMine(true,AiState.Busy,false,TimeSpan.FromSeconds(31),30),"AI work pauses mining");
Check(!policy.ShouldMine(true,AiState.Idle,false,TimeSpan.FromSeconds(50),30),"idle timer resets after AI");
Check(!policy.ShouldMine(true,AiState.Unknown,false,TimeSpan.FromSeconds(90),30),"unavailable probe pauses mining");
Check(policy.ShouldMine(false,AiState.Busy,false,TimeSpan.FromSeconds(100),30),"manual mode bypasses AI");
Check(!policy.ShouldMine(false,AiState.Idle,true,TimeSpan.FromSeconds(101),30),"manual mode still yields to another miner");
Check(InferenceProbe.Parse("[{\"status\":\"idle\",\"queued\":0}]")==AiState.Idle,"loaded idle model can share");
Check(InferenceProbe.Parse("[{\"status\":\"idle\",\"queued\":1}]")==AiState.Busy,"queued request reserves GPU");
Check(InferenceProbe.Parse("[{\"status\":\"computingEmbedding\",\"queued\":0}]")==AiState.Busy,"embedding inference reserves GPU");
Check(InferenceProbe.Parse("[{\"status\":\"idle\"}]")==AiState.Unknown,"incomplete inference state fails closed");
Check(InferenceProbe.Parse("[{\"status\":\"generating\",\"queued\":0,\"deviceIdentifier\":\"remote\"}]")==AiState.Idle,"remote inference excluded");
var stats=new MinerStats();stats.Read("INFO cl_device(0): temp:64C, hashrate: 123.45 kH/s");stats.Read("INFO cu_device(0): hashrate: 1.00 MH/s");stats.Read("accepted: 4/5 (80%), total hashrate: 1123.45 kH/s");
Check(Math.Abs(stats.Hashrate-1123.45)<.001 && stats.Accepted==4 && stats.Total==5,"multi-device rates and share counts parsed without double counting");
stats.Read("WARN cl_device(0):Monitoring has been disabled. No backends available!");
Check(stats.MonitoringUnavailable,"unsupported temperature monitoring is surfaced");

string scratch=Path.Combine(Path.GetTempPath(),"OcmTests-"+Guid.NewGuid());Directory.CreateDirectory(scratch);
async Task<int> ReadPid(string path) {for(int i=0;i<100 && !File.Exists(path);i++)await Task.Delay(50);using var doc=JsonDocument.Parse(File.ReadAllText(path));return doc.RootElement.GetProperty("Pid").GetInt32();}
bool Running(int pid) {try{using var p=Process.GetProcessById(pid);return !p.HasExited;}catch(ArgumentException){return false;}}
string receipt=Path.Combine(scratch,"arguments.json");
using(var worker=new MinerWorker(Environment.ProcessPath!,"--fixture",receipt,"spaces in argument", "trailing slash\\", "embedded\"quote", ""))
{
    worker.Start();int pid=await ReadPid(receipt);Check(worker.Running && worker.ProcessId==pid,"owned process launches");
    using var doc=JsonDocument.Parse(File.ReadAllText(receipt));var actual=doc.RootElement.GetProperty("Args").EnumerateArray().Select(x=>x.GetString()).ToArray();
    Check(actual.SequenceEqual(new[]{"spaces in argument","trailing slash\\","embedded\"quote",""}),"Windows command arguments round-trip without shell parsing");
    worker.Stop();Check(!Running(pid),"stop waits for owned worker to exit");
    worker.Start();await Task.Delay(100);Check(worker.Running,"owned worker can resume");
}
string orphan=Path.Combine(scratch,"crash.json");
var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};start.ArgumentList.Add("--owner");start.ArgumentList.Add(orphan);
using(var host=Process.Start(start)!) {int child=await ReadPid(orphan);host.Kill();await host.WaitForExitAsync();for(int i=0;i<40 && Running(child);i++)await Task.Delay(50);Check(!Running(child),"controller crash terminates mining child through Windows job ownership");}

var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;
var server=Task.Run(async()=>{using var c=await listener.AcceptTcpClientAsync();using var reader=new StreamReader(c.GetStream(),leaveOpen:true);string? request=await reader.ReadLineAsync();if(request?.Contains("mining.subscribe")!=true)throw new Exception("bad subscribe");await c.GetStream().WriteAsync(Encoding.UTF8.GetBytes("{\"id\":1,\"result\":[[],\"abc\",4],\"error\":null}\n"));});
string connection=await MinerSetup.CheckPoolAsync($"stratum+tcp://127.0.0.1:{port}",CancellationToken.None);await server;listener.Stop();Check(connection.StartsWith("Stratum responding"),"pool check parses a real socket response");
string fixture=Path.Combine(scratch,"OcmFixture.exe");File.Copy(Environment.ProcessPath!,fixture);
string dataFile=Path.Combine(scratch,"test.dat");using(var file=File.Create(dataFile))file.SetLength(MinerSetup.DataBytes);
AiState probeState=AiState.Idle;
var liveSettings=new Settings {Address="vtc1q49ed04t8vqclja67awkjjgxdre5n0exzumlm62",MinerPath=fixture,DataPath=dataFile,GpuKey="cl:0",AutoAi=true,IdleSeconds=5};
var liveArgs=liveSettings.MinerArguments();
Check(liveArgs[Array.IndexOf(liveArgs,"--user")+1]==liveSettings.Address+".ocm" && !liveArgs.Contains("--coinbase-addr") && !liveArgs.Contains("--benchmark"),"live mining directs payouts only to the user-supplied wallet");
var active=new TaskCompletionSource<MiningStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
var paused=new TaskCompletionSource<MiningStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
await using(var controller=new MiningController { Probe=(_,_)=>Task.FromResult(probeState) })
{
    controller.Changed+=s=>{if(s.Hashrate>0)active.TrySetResult(s);if(s.State=="Paused · AI has the GPU")paused.TrySetResult(s);};
    controller.Start(liveSettings);
    var mining=await active.Task.WaitAsync(TimeSpan.FromSeconds(15));
    Check(mining.Hashrate==125.50 && mining.Accepted==2 && mining.Total==3,"controller consumes real worker log and reports live statistics");
    int fixturePid=int.Parse(File.ReadAllText(Path.Combine(scratch,"fixture-pid.txt")));
    probeState=AiState.Busy;await paused.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Check(!Running(fixturePid),"controller stops actual worker when AI becomes busy");
    await controller.StopAsync();probeState=AiState.Idle;await Task.Delay(1500);
    Check(!controller.Enabled && !Running(fixturePid),"manual stop while paused does not resume later");
}
var mediaRoot=Path.Combine(scratch,"media-tests");Directory.CreateDirectory(mediaRoot);
File.WriteAllText(Path.Combine(mediaRoot,"one.mp3"),"fixture");File.WriteAllText(Path.Combine(mediaRoot,"three.mp3"),"fixture");
var songs=new List<Track>{new(){Id="one",File="one.mp3"},new(){Id="missing",File="two.mp3"},new(){Id="three",File="three.mp3"}};
Check(Throws(()=>MediaCatalog.LocalPath(mediaRoot,"../escape.mp3")),"media cannot escape the app folder");
Check(Throws(()=>MediaCatalog.LocalPath(mediaRoot,Path.GetFullPath("outside.mp3"))),"absolute media paths rejected");
Check(MediaCatalog.NextPlayable(songs,mediaRoot,0,1,false,new Random(1))==2,"playlist skips missing recordings");
Check(MediaCatalog.NextPlayable(songs,mediaRoot,2,1,false,new Random(1))==0,"repeat wraps to the first available song");
Check(MediaCatalog.NextPlayable(songs,mediaRoot,2,1,false,new Random(1),false)==-1,"repeat off stops at the playlist end");
Check(MediaCatalog.NextPlayable(songs,mediaRoot,0,1,true,new Random(1))==2,"shuffle avoids current and missing recordings");
Check(MediaCatalog.NextPlayable([],mediaRoot,-1,1,false,new Random(1))==-1,"empty playlist stops without looping");
var prefs=new AudioPreferences{MusicVolume=.5,EffectsMuted=true,VoiceMuted=true};
Check(prefs.EffectiveMusic(false)==.5,"effects and commentary mutes leave music independent");
prefs.MusicMuted=true;Check(prefs.EffectiveMusic(false)==0 && prefs.EffectiveMusic(true)==0,"music mute remains silent while ducking changes");
prefs.MusicMuted=false;Check(Math.Abs(prefs.EffectiveMusic(true)-.11)<.0001,"commentary ducks rather than stops music");
Check(AudioPreferences.Level(double.NaN)==0 && AudioPreferences.Level(2)==1 && AudioPreferences.Level(-1)==0,"invalid saved volume values safely clamped");
var restored=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(new Settings{Audio=prefs}))!;
Check(restored.Audio.EffectsMuted && restored.Audio.VoiceMuted && restored.Audio.MusicVolume==.5,"independent audio preferences survive settings round-trip");
await CoreTests.RunAsync(Check, scratch);
Directory.Delete(scratch,true);
Console.WriteLine($"\n{passed} tests passed.");
