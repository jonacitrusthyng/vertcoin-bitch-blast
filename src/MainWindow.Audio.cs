using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace VertcoinOcm;

public partial class MainWindow
{
    private AudioEngine? audio;
    private readonly DispatcherTimer audioTimer=new(){Interval=TimeSpan.FromMilliseconds(250)};
    private List<Track> tracks=[];
    private int currentTrack=-1;
    private readonly HashSet<int> playedInCycle=[];
    private bool audioReady, seekUpdating, seeking;
    private DateTime lastComment=DateTime.MinValue;
    private string lastMiningState="";
    private int lastAccepted;
    private static string MediaRoot=>AppContext.BaseDirectory;
    private void InitializeAudio()
    {
        settings.Audio??=new();
        audio=new AudioEngine(settings.Audio);
        audio.Changed+=RefreshAudio;
        audio.Failed+=message=>{JukeboxStatus.Text=message;FooterStatus.Text=message;};
        audio.Ended+=()=>NextTrack(1,true);
        MusicMute.IsChecked=settings.Audio.MusicMuted;EffectsMute.IsChecked=settings.Audio.EffectsMuted;VoiceMute.IsChecked=settings.Audio.VoiceMuted;
        MusicVolume.Value=AudioPreferences.Level(settings.Audio.MusicVolume)*100;
        EffectsVolume.Value=AudioPreferences.Level(settings.Audio.EffectsVolume)*100;
        VoiceVolume.Value=AudioPreferences.Level(settings.Audio.VoiceVolume)*100;
        ShuffleToggle.IsChecked=settings.Audio.Shuffle;RepeatToggle.IsChecked=settings.Audio.Repeat;
        try{tracks=MediaCatalog.Load(MediaRoot);}catch(Exception ex){JukeboxStatus.Text="Music catalog unavailable: "+ex.Message;}
        TrackList.ItemsSource=tracks;
        int available=tracks.Count(t=>t.AvailableIn(MediaRoot));
        AlbumStatus.Text=$"{available} recordings installed · {tracks.Count} songs in the collection";
        TrackList.SelectedIndex=tracks.FindIndex(t=>t.AvailableIn(MediaRoot));
        audioTimer.Tick+=(_,_)=>RefreshAudio();audioTimer.Start();
        audioReady=true;RefreshAudio();
    }
    private void RefreshAudio()
    {
        if(audio==null||closed)return;
        string title=currentTrack>=0?tracks[currentTrack].Title:"Pick a song. Crack a smile.";
        NowPlaying.Text=title;
        PlayMusicButton.Content=audio.Playing?"Ⅱ  Pause":"▶  Play";
        DockPlayButton.Content=audio.Playing?"Ⅱ":"▶";
        PlaybackClock.Text=$"{audio.Position:mm\\:ss} / {audio.Duration:mm\\:ss}";
        if(!seeking){seekUpdating=true;SeekBar.Maximum=Math.Max(1,audio.Duration.TotalSeconds);SeekBar.Value=audio.Position.TotalSeconds;seekUpdating=false;}
        SeekBar.IsEnabled=audio.Duration>TimeSpan.Zero;
        UpdateGalleryAudio();
        MasterMuteButton.Content=settings.Audio.MusicMuted&&settings.Audio.EffectsMuted&&settings.Audio.VoiceMuted?"Unmute all":"Mute all";
    }
    private void AudioChanged(object sender,RoutedEventArgs e)
    {
        if(!audioReady||audio==null)return;
        settings.Audio.MusicMuted=MusicMute.IsChecked==true;settings.Audio.EffectsMuted=EffectsMute.IsChecked==true;settings.Audio.VoiceMuted=VoiceMute.IsChecked==true;
        settings.Audio.Shuffle=ShuffleToggle.IsChecked==true;settings.Audio.Repeat=RepeatToggle.IsChecked==true;
        audio.Apply(settings.Audio);SaveAudio();RefreshAudio();
    }
    private void AudioVolumeChanged(object sender,RoutedPropertyChangedEventArgs<double> e)
    {
        if(!audioReady||audio==null)return;
        settings.Audio.MusicVolume=MusicVolume.Value/100;settings.Audio.EffectsVolume=EffectsVolume.Value/100;settings.Audio.VoiceVolume=VoiceVolume.Value/100;
        audio.Apply(settings.Audio);
    }
    private void VolumeFinished(object sender,RoutedEventArgs e){if(audioReady)SaveAudio();}
    private void SaveAudio(){try{settings.Save();}catch(Exception ex){FooterStatus.Text="Could not save sound preferences: "+ex.Message;}}
    private void MasterMuteClick(object s,RoutedEventArgs e)
    {
        bool mute=!(settings.Audio.MusicMuted&&settings.Audio.EffectsMuted&&settings.Audio.VoiceMuted);
        audioReady=false;MusicMute.IsChecked=mute;EffectsMute.IsChecked=mute;VoiceMute.IsChecked=mute;audioReady=true;AudioChanged(s,e);
    }
    private void JukeboxClick(object s,RoutedEventArgs e){ShowPanel(JukeboxPanel);Comment("jukebox","Welcome to the nut house. I brought the playlist.");}
    private void TrackSelected(object s,SelectionChangedEventArgs e)
    {
        if(TrackList.SelectedItem is not Track track)return;
        SelectedTitle.Text=track.Title;TrackLyrics.Text=track.Lyrics;TrackCredit.Text=track.Credit;
        JukeboxStatus.Text=track.AvailableIn(MediaRoot)?"Ready to play locally.":"Lyrics are included. This song’s recording is not installed yet.";
        if(track.Cover!="")try{var path=MediaCatalog.LocalPath(MediaRoot,track.Cover);if(File.Exists(path)){var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.UriSource=new Uri(path);bitmap.EndInit();CoverImage.Source=bitmap;}}catch{ /* Keep the existing cover if an optional image is missing. */ }
    }
    private void PlayTrack(int index)
    {
        if(audio==null||index<0||index>=tracks.Count)return;
        if(!tracks[index].AvailableIn(MediaRoot)){JukeboxStatus.Text="This recording is not installed. Select an available song.";ShowPanel(JukeboxPanel);return;}
        try{currentTrack=index;playedInCycle.Add(index);TrackList.SelectedIndex=index;audio.Play(tracks[index].PathIn(MediaRoot));JukeboxStatus.Text="Playing locally · no streaming or account required.";RefreshAudio();}
        catch(Exception ex){JukeboxStatus.Text=ex.Message;}
    }
    private void PlayMusicClick(object s,RoutedEventArgs e)
    {
        if(audio==null)return;
        if(audio.Playing && TrackList.SelectedIndex==currentTrack){audio.Pause();JukeboxStatus.Text="Paused. Your place is saved.";return;}
        int index=TrackList.SelectedIndex;
        if(index<0)index=MediaCatalog.NextPlayable(tracks,MediaRoot,-1,1,false,Random.Shared);
        PlayTrack(index);
    }
    private void TrackDoubleClick(object s,MouseButtonEventArgs e)=>PlayTrack(TrackList.SelectedIndex);
    private void DockPlayClick(object s,RoutedEventArgs e){if(audio?.Playing==true){audio.Pause();JukeboxStatus.Text="Paused. Your place is saved.";}else if(currentTrack>=0)PlayTrack(currentTrack);else PlayMusicClick(s,e);}
    private void StopMusicClick(object s,RoutedEventArgs e){audio?.Stop();JukeboxStatus.Text="Stopped. Press Play when you’re ready.";}
    private void PreviousMusicClick(object s,RoutedEventArgs e)=>NextTrack(-1);
    private void NextMusicClick(object s,RoutedEventArgs e)=>NextTrack(1);
    private void NextTrack(int direction,bool automatic=false)
    {
        int next;
        if(automatic&&settings.Audio.Shuffle&&!settings.Audio.Repeat)
        {
            var remaining=Enumerable.Range(0,tracks.Count).Where(i=>!playedInCycle.Contains(i)&&tracks[i].AvailableIn(MediaRoot)).ToArray();
            next=remaining.Length==0?-1:remaining[Random.Shared.Next(remaining.Length)];
        }
        else next=MediaCatalog.NextPlayable(tracks,MediaRoot,currentTrack,direction,settings.Audio.Shuffle&&direction>0,Random.Shared,!automatic||settings.Audio.Repeat);
        if(next<0){JukeboxStatus.Text="Playlist finished. Press Play to listen again.";playedInCycle.Clear();RefreshAudio();return;}
        PlayTrack(next);
    }
    private void SeekStarted(object s,MouseButtonEventArgs e)=>seeking=true;
    private void SeekFinished(object s,MouseButtonEventArgs e){if(audio!=null)audio.Position=TimeSpan.FromSeconds(SeekBar.Value);seeking=false;}
    private void SeekChanged(object s,RoutedPropertyChangedEventArgs<double> e){if(!seekUpdating&&!seeking&&audio!=null)audio.Position=TimeSpan.FromSeconds(e.NewValue);}
    private void Comment(string id,string text,bool force=false)
    {
        if(audio==null||closed)return;
        if(!force&&DateTime.UtcNow-lastComment<TimeSpan.FromSeconds(3))return;
        lastComment=DateTime.UtcNow;EmiCaption.Text="Emi: "+text;
        audio.Say(Path.Combine(MediaRoot,"Media","Voice",id+".wav"));
    }
    private void PlayEffect(string id)=>audio?.PlayEffect(Path.Combine(MediaRoot,"Media","Effects",id+".wav"));
    private void TestVoiceClick(object s,RoutedEventArgs e)=>Comment("welcome","Welcome to Bitch Blast. The walnut has left the trailer.",true);
    private void TestEffectsClick(object s,RoutedEventArgs e)=>PlayEffect("blast");
    private void OnMiningAudio(MiningStatus status)
    {
        if(status.State!=lastMiningState)
        {
            if(status.Active&&!lastMiningState.StartsWith("Mining"))Comment("mining","The GPU is working. Finally, somebody in this trailer has a job.");
            else if(status.State.Contains("AI",StringComparison.OrdinalIgnoreCase)&&!status.Active)Comment("ai_pause","AI needs the GPU. Scoot over, mining. The diva has arrived.");
            lastMiningState=status.State;
        }
        if(status.Accepted>lastAccepted&&lastAccepted==0){PlayEffect("coin");Comment("share","An accepted share. Receipts, bitch.");}
        lastAccepted=status.Accepted;
    }
}

