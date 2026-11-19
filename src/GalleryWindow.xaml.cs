using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace VertcoinOcm;

/// <summary>A modeless picture window; the main window remains the owner of mining and audio.</summary>
public partial class GalleryWindow : Window
{
    private sealed record Picture(string Title, string Key, Uri Source);
    private readonly List<Picture> pictures = [];
    private readonly DispatcherTimer slideshow = new() { Interval = TimeSpan.FromSeconds(12) };
    private int pictureIndex;
    private bool isClosed, hudVisible = true;
    private Rect restoreBounds;
    private WindowState restoreState;
    public bool IsFullScreen { get; private set; }
    public event Action? ToggleMusicRequested;
    public event Action? ToggleMuteRequested;

    public GalleryWindow()
    {
        InitializeComponent();
        LoadPictures();
        slideshow.Tick += (_, _) => AdvancePicture(1);
        ShowPicture(0);
    }

    private static Uri Embedded(string path) => new("pack://application:,,,/Assets/" + path, UriKind.Absolute);

    private void LoadPictures()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var track in MediaCatalog.Load(AppContext.BaseDirectory))
            {
                if (string.IsNullOrWhiteSpace(track.Cover)) continue;
                string key = track.Cover.Replace('\\', '/');
                if (!seen.Add(key)) continue;
                if (key.Equals("Assets/emi-host.png", StringComparison.OrdinalIgnoreCase))
                {
                    pictures.Add(new("Emi · portrait", key, Embedded("emi-host.png")));
                    continue;
                }
                string path = MediaCatalog.LocalPath(AppContext.BaseDirectory, key);
                if (File.Exists(path)) pictures.Add(new(track.Title, key, new Uri(path, UriKind.Absolute)));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        {
            // The two embedded pictures still work if an optional media pack is absent or invalid.
        }
        if (seen.Add("Assets/emi-host.png")) pictures.Add(new("Emi · portrait", "Assets/emi-host.png", Embedded("emi-host.png")));
        pictures.Add(new("The walnut has left the trailer", "Assets/walnut-trailer.jpg", Embedded("walnut-trailer.jpg")));
    }

    private void ShowPicture(int index)
    {
        if (isClosed || pictures.Count == 0) return;
        pictureIndex = ((index % pictures.Count) + pictures.Count) % pictures.Count;
        var picture = pictures[pictureIndex];
        ImageTitle.Text = picture.Title;
        ImageCount.Text = $"{pictureIndex + 1:00} / {pictures.Count:00}";
        System.Windows.Automation.AutomationProperties.SetName(GalleryImage, picture.Title);
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = picture.Source;
            bitmap.EndInit();
            bitmap.Freeze();
            GalleryImage.Source = bitmap;
            ImageError.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException or System.IO.FileFormatException)
        {
            GalleryImage.Source = null;
            ImageError.Text = "This picture is unavailable. Use Next to continue.";
            ImageError.Visibility = Visibility.Visible;
        }
    }

    public void SelectImageForTrack(string coverPath)
    {
        if (isClosed || string.IsNullOrWhiteSpace(coverPath)) return;
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => SelectImageForTrack(coverPath)); return; }
        int index = pictures.FindIndex(p => p.Key.Equals(coverPath.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
        if (index >= 0) ShowPicture(index);
    }

    public void UpdateHud(string state, double hashRate, int accepted, int total, string ai, string pool, string nowPlaying)
    {
        if (isClosed) return;
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => UpdateHud(state, hashRate, accepted, total, ai, pool, nowPlaying)); return; }
        HudState.Text = string.IsNullOrWhiteSpace(state) ? "Miner idle" : state;
        HudHashrate.Text = double.IsFinite(hashRate) && hashRate > 0
            ? hashRate.ToString("N2", CultureInfo.CurrentCulture) + " kH/s" : "— kH/s";
        HudShares.Text = $"{Math.Max(0, accepted):N0} accepted";
        HudRejected.Text = total > 0 ? $"{Math.Max(0, total - accepted):N0} rejected · {total:N0} submitted" : "No shares submitted yet";
        HudAi.Text = string.IsNullOrWhiteSpace(ai) ? "Not monitoring" : ai;
        HudPool.Text = "POOL // " + (string.IsNullOrWhiteSpace(pool) ? "Not selected" : pool);
        HudSong.Text = "MUSIC // " + (string.IsNullOrWhiteSpace(nowPlaying) ? "Choose a song in the jukebox" : nowPlaying);
    }

    public void UpdateNode(string summary)
    {
        if (isClosed) return;
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => UpdateNode(summary)); return; }
        HudNode.Text = "NODE // " + (string.IsNullOrWhiteSpace(summary) ? "Not connected" : summary);
    }

    public void SetAudioState(bool playing, bool allMuted)
    {
        if (isClosed) return;
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => SetAudioState(playing, allMuted)); return; }
        GalleryMusicButton.Content = playing ? "Ⅱ Pause music" : "▶ Music";
        GalleryMuteButton.Content = allMuted ? "Unmute all" : "Mute all";
    }

    public void SetFullScreen(bool enabled)
    {
        if (isClosed || enabled == IsFullScreen) return;
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => SetFullScreen(enabled)); return; }
        if (enabled)
        {
            restoreState = WindowState;
            restoreBounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
            IsFullScreen = true;
        }
        else
        {
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            if (!restoreBounds.IsEmpty)
            {
                Left = restoreBounds.Left; Top = restoreBounds.Top;
                Width = Math.Max(MinWidth, restoreBounds.Width); Height = Math.Max(MinHeight, restoreBounds.Height);
            }
            WindowState = restoreState;
            IsFullScreen = false;
        }
        FullScreenButton.Content = enabled ? "⛶ Windowed" : "⛶ Fullscreen";
    }

    private void AdvancePicture(int direction) => ShowPicture(pictureIndex + direction);
    private void PreviousImageClick(object sender, RoutedEventArgs e) => AdvancePicture(-1);
    private void NextImageClick(object sender, RoutedEventArgs e) => AdvancePicture(1);
    private void FullScreenClick(object sender, RoutedEventArgs e) => SetFullScreen(!IsFullScreen);
    private void BackClick(object sender, RoutedEventArgs e) { Owner?.Activate(); Close(); }
    private void MusicClick(object sender, RoutedEventArgs e) => ToggleMusicRequested?.Invoke();
    private void MuteClick(object sender, RoutedEventArgs e) => ToggleMuteRequested?.Invoke();
    private void SlideshowClick(object sender, RoutedEventArgs e)
    {
        if (slideshow.IsEnabled) slideshow.Stop(); else slideshow.Start();
        SlideshowButton.Content = slideshow.IsEnabled ? "Slideshow: on" : "Slideshow: off";
    }
    private void HudClick(object sender, RoutedEventArgs e)
    {
        hudVisible = !hudVisible;
        TelemetryHud.Visibility = hudVisible ? Visibility.Visible : Visibility.Collapsed;
        HudButton.Content = hudVisible ? "Hide HUD" : "Show HUD";
    }
    private void ImageMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { SetFullScreen(!IsFullScreen); e.Handled = true; }
    }
    private void GalleryKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11) { SetFullScreen(!IsFullScreen); e.Handled = true; }
        else if (e.Key == Key.Escape) { if (IsFullScreen) SetFullScreen(false); else Close(); e.Handled = true; }
        else if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.Right) { AdvancePicture(1); e.Handled = true; }
        else if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.Left) { AdvancePicture(-1); e.Handled = true; }
    }
    private void GalleryClosed(object? sender, EventArgs e)
    {
        isClosed = true;
        slideshow.Stop();
        GalleryImage.Source = null;
        ToggleMusicRequested = null;
        ToggleMuteRequested = null;
    }
}
