using System.IO;
using System.Windows.Media;
namespace VertcoinOcm;

// Three independent channels; no network requests, microphone or background service.
public sealed class AudioEngine : IDisposable
{
    private readonly MediaPlayer music=new(), voice=new(), effects=new();
    private AudioPreferences preferences;
    private bool speaking, disposed, opened, wantsPlayback;
    public bool Playing { get; private set; }
    public string CurrentFile { get; private set; }="";
    public event Action? Changed;
    public event Action? Ended;
    public event Action<string>? Failed;
    public TimeSpan Position { get => opened?music.Position:TimeSpan.Zero; set { if(opened && music.NaturalDuration.HasTimeSpan) music.Position=TimeSpan.FromSeconds(Math.Clamp(value.TotalSeconds,0,Duration.TotalSeconds)); } }
    public TimeSpan Duration => opened && music.NaturalDuration.HasTimeSpan?music.NaturalDuration.TimeSpan:TimeSpan.Zero;
    public AudioEngine(AudioPreferences prefs)
    {
        preferences=prefs;
        music.MediaOpened+=(_,_)=>{opened=true;if(wantsPlayback){music.Play();Playing=true;}Changed?.Invoke();};
        music.MediaEnded+=(_,_)=>{music.Stop();Playing=false;wantsPlayback=false;Changed?.Invoke();Ended?.Invoke();};
        music.MediaFailed+=(_,e)=>{music.Close();opened=false;wantsPlayback=false;Playing=false;CurrentFile="";Failed?.Invoke("This audio could not play: "+e.ErrorException.Message);Changed?.Invoke();};
        voice.MediaEnded+=(_,_)=>EndVoice();
        voice.MediaFailed+=(_,_)=>EndVoice();
        effects.MediaFailed+=(_,_)=>effects.Close();
        Apply(prefs);
    }
    public void Apply(AudioPreferences prefs)
    {
        preferences=prefs;
        if(prefs.VoiceMuted)EndVoice();
        if(prefs.EffectsMuted)effects.Stop();
        music.Volume=prefs.EffectiveMusic(speaking);
        voice.Volume=prefs.VoiceMuted?0:AudioPreferences.Level(prefs.VoiceVolume);
        effects.Volume=prefs.EffectsMuted?0:AudioPreferences.Level(prefs.EffectsVolume);
    }
    public void Play(string path)
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        if(!File.Exists(path))throw new FileNotFoundException("The recording is not installed.",path);
        if(CurrentFile==path && opened){wantsPlayback=true;Playing=true;music.Play();Changed?.Invoke();return;}
        music.Close();opened=false;Playing=false;wantsPlayback=true;CurrentFile=path;
        music.Open(new Uri(path,UriKind.Absolute));Changed?.Invoke();
    }
    public void Pause(){wantsPlayback=false;music.Pause();Playing=false;Changed?.Invoke();}
    public void Stop(){wantsPlayback=false;music.Stop();Playing=false;Changed?.Invoke();}
    public void PlayEffect(string path){if(disposed||preferences.EffectsMuted||!File.Exists(path))return;effects.Close();effects.Open(new Uri(path,UriKind.Absolute));effects.Play();}
    public void Say(string path)
    {
        if(disposed||preferences.VoiceMuted||!File.Exists(path))return;
        voice.Close();speaking=true;music.Volume=preferences.EffectiveMusic(true);
        voice.Open(new Uri(path,UriKind.Absolute));voice.Play();
    }
    private void EndVoice(){voice.Close();speaking=false;music.Volume=preferences.EffectiveMusic(false);}
    public void Dispose(){if(disposed)return;disposed=true;music.Close();voice.Close();effects.Close();Playing=false;}
}

