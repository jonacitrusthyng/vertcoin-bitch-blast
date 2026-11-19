using System.Windows;
namespace VertcoinOcm;

public partial class MainWindow
{
    private GalleryWindow? gallery;
    private MiningStatus latestMiningStatus = new("Miner idle", "", false, Ai: "Not monitoring");
    private string nodeHudSummary = "Not connected";

    private void GalleryClick(object sender, RoutedEventArgs e)
    {
        if (gallery is null)
        {
            gallery = new GalleryWindow { Owner = this };
            gallery.ToggleMusicRequested += () => DockPlayClick(this, new RoutedEventArgs());
            gallery.ToggleMuteRequested += () => MasterMuteClick(this, new RoutedEventArgs());
            gallery.Closed += (_, _) => gallery = null;
            if (TrackList.SelectedItem is Track track) gallery.SelectImageForTrack(track.Cover);
            UpdateGalleryHud();
            UpdateGalleryAudio();
            gallery.Show();
        }
        else { if (gallery.WindowState == WindowState.Minimized) gallery.WindowState = WindowState.Normal; gallery.Activate(); }
    }
    private void UpdateGalleryHud()
    {
        if (gallery is null) return;
        var status = latestMiningStatus;
        string ai = settings.AutoAi ? status.Ai : "Manual mining mode";
        gallery.UpdateHud(status.State, status.Hashrate, status.Accepted, status.Total, ai, settings.Pool.Name, NowPlaying.Text);
        gallery.UpdateNode(nodeHudSummary);
    }
    private void UpdateGalleryAudio()
    {
        if (gallery is null) return;
        gallery.SetAudioState(audio?.Playing == true, settings.Audio.MusicMuted && settings.Audio.EffectsMuted && settings.Audio.VoiceMuted);
        UpdateGalleryHud();
    }
}
