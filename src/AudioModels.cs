using System.IO;
using System.Text.Json;

namespace VertcoinOcm;

public sealed class Track
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string File { get; set; } = "";
    public string Cover { get; set; } = "";
    public string Lyrics { get; set; } = "";
    public string Credit { get; set; } = "";
    public string PathIn(string root) => MediaCatalog.LocalPath(root, File);
    public bool AvailableIn(string root) => System.IO.File.Exists(PathIn(root));
    public override string ToString() => Title;
}

public static class MediaCatalog
{
    // Catalog paths are bundled files, never URLs or executable commands.
    public static string LocalPath(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || System.IO.Path.IsPathRooted(relative)) throw new InvalidDataException("Invalid media path.");
        var parent = System.IO.Path.GetFullPath(root).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
        var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(parent, relative));
        if (!full.StartsWith(parent, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Media path leaves the app folder.");
        return full;
    }
    public static List<Track> Load(string root)
    {
        var tracks = JsonSerializer.Deserialize<List<Track>>(System.IO.File.ReadAllText(System.IO.Path.Combine(root,"Media","catalog.json"))) ?? [];
        if (tracks.Select(t=>t.Id).Distinct().Count()!=tracks.Count) throw new InvalidDataException("Duplicate track ID.");
        foreach (var t in tracks) { _=t.PathIn(root); if(t.Cover!="") _=LocalPath(root,t.Cover); }
        return tracks;
    }
    public static int NextPlayable(IReadOnlyList<Track> tracks, string root, int current, int direction, bool shuffle, Random random, bool wrap=true)
    {
        var playable = Enumerable.Range(0,tracks.Count).Where(i=>tracks[i].AvailableIn(root)).ToArray();
        if(playable.Length==0)return -1;
        if(shuffle) { var choices=playable.Where(i=>i!=current).ToArray(); return choices.Length==0?playable[0]:choices[random.Next(choices.Length)]; }
        for(int step=1;step<=tracks.Count;step++) { int raw=current+Math.Sign(direction)*step;if(!wrap&&(raw<0||raw>=tracks.Count))return -1;int i=(raw%tracks.Count+tracks.Count)%tracks.Count; if(playable.Contains(i))return i; }
        return -1;
    }
}

public sealed class AudioPreferences
{
    public bool MusicMuted { get; set; }
    public bool EffectsMuted { get; set; }
    public bool VoiceMuted { get; set; }
    public double MusicVolume { get; set; } = .35;
    public double EffectsVolume { get; set; } = .4;
    public double VoiceVolume { get; set; } = .7;
    public bool Shuffle { get; set; }
    public bool Repeat { get; set; } = true;
    public static double Level(double value) => double.IsFinite(value)?Math.Clamp(value,0,1):0;
    public double EffectiveMusic(bool speaking) => MusicMuted?0:Level(MusicVolume)*(speaking?.22:1);
}
