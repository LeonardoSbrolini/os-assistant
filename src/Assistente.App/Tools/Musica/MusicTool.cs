using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;

namespace Assistente.App.Tools.Musica;

public sealed class MusicTool : ITool
{
    public string Name => "Tocando agora";
    public string Category => "Música";
    public string Glyph => "";
    public FrameworkElement CreateView() => new MusicView();
}

/// <summary>
/// Controla o que está tocando no Windows (YouTube Music no navegador ou no app,
/// Spotify, etc.) pelo controle de mídia do sistema.
/// </summary>
internal sealed class MusicView : UserControl
{
    private const string YouTubeMusicUrl = "https://music.youtube.com";

    private readonly DispatcherTimer _progressTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly Border _cover = new()
    {
        Width = 150,
        Height = 150,
        CornerRadius = new CornerRadius(16),
        Margin = new Thickness(0, 0, 18, 0),
        VerticalAlignment = VerticalAlignment.Top,
    };
    private readonly TextBlock _title = new() { FontSize = 20, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _artist = new() { FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
    private readonly TextBlock _source = Ui.Caption("");
    private readonly ProgressBar _progress = new() { Maximum = 1, Height = 5, Margin = new Thickness(0, 14, 0, 4) };
    private readonly TextBlock _time = Ui.Caption("");
    private readonly Button _playPause;
    private readonly WrapPanel _sessions = new();
    private readonly TextBlock _hint = Ui.Caption("");

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private GlobalSystemMediaTransportControlsSessionTimelineProperties? _timeline;
    private bool _playing;

    public MusicView()
    {
        _artist.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        SetCover(null);

        _playPause = RoundButton("", 56, primary: true, "Tocar / pausar", () => Control(s => s.TryTogglePlayPauseAsync(), VkMediaPlayPause));
        var prev = RoundButton("", 42, false, "Anterior", () => Control(s => s.TrySkipPreviousAsync(), VkMediaPrev));
        var next = RoundButton("", 42, false, "Próxima", () => Control(s => s.TrySkipNextAsync(), VkMediaNext));
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        controls.Children.Add(prev);
        controls.Children.Add(_playPause);
        controls.Children.Add(next);

        var volume = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        volume.Children.Add(RoundButton("", 34, false, "Mudo", () => PressKey(VkVolumeMute)));
        volume.Children.Add(RoundButton("", 34, false, "Diminuir volume", () => PressKey(VkVolumeDown, 2)));
        volume.Children.Add(RoundButton("", 34, false, "Aumentar volume", () => PressKey(VkVolumeUp, 2)));

        var info = new StackPanel();
        info.Children.Add(_title);
        info.Children.Add(_artist);
        info.Children.Add(_source);
        info.Children.Add(_progress);
        info.Children.Add(_time);
        info.Children.Add(controls);
        info.Children.Add(volume);

        var player = new DockPanel();
        DockPanel.SetDock(_cover, Dock.Left);
        player.Children.Add(_cover);
        player.Children.Add(info);

        Content = Ui.Scroll(Ui.Stack(
            Ui.Title("Música"),
            Ui.Card("Tocando agora", "", player),
            Ui.Card("Players abertos", "", _sessions, _hint),
            Ui.Inline(
                Ui.Btn("Abrir YouTube Music", () => Process.Start(new ProcessStartInfo(YouTubeMusicUrl) { UseShellExecute = true }), primary: true)),
            Ui.Caption("Funciona com o YouTube Music no navegador (Chrome, Edge…) ou no app, e também com Spotify, " +
                       "players de vídeo e outros programas que aparecem no controle de mídia do Windows.")));

        _progressTimer.Tick += (_, _) => UpdateProgress();
        IsVisibleChanged += async (_, _) =>
        {
            if (IsVisible)
            {
                await EnsureManagerAsync();
                await RefreshAsync();
                _progressTimer.Start();
            }
            else
            {
                _progressTimer.Stop();
            }
        };
    }

    // ---------- Sessões de mídia do Windows ----------

    private async Task EnsureManagerAsync()
    {
        if (_manager is not null) return;
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.SessionsChanged += (_, _) => OnUi(RefreshAsync);
            _manager.CurrentSessionChanged += (_, _) => OnUi(RefreshAsync);
        }
        catch (Exception)
        {
            _hint.Text = "O controle de mídia do Windows não está disponível neste computador. Os botões usam as teclas de mídia.";
        }
    }

    private void OnUi(Func<Task> action) => Dispatcher.BeginInvoke(new Func<Task>(action));

    private void Attach(GlobalSystemMediaTransportControlsSession? session)
    {
        if (_session == session) return;
        if (_session is not null)
        {
            _session.MediaPropertiesChanged -= OnMediaChanged;
            _session.PlaybackInfoChanged -= OnPlaybackChanged;
            _session.TimelinePropertiesChanged -= OnTimelineChanged;
        }
        _session = session;
        if (_session is not null)
        {
            _session.MediaPropertiesChanged += OnMediaChanged;
            _session.PlaybackInfoChanged += OnPlaybackChanged;
            _session.TimelinePropertiesChanged += OnTimelineChanged;
        }
    }

    private void OnMediaChanged(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e) => OnUi(RefreshMediaAsync);
    private void OnPlaybackChanged(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e) => OnUi(() => { RefreshPlayback(); return Task.CompletedTask; });
    private void OnTimelineChanged(GlobalSystemMediaTransportControlsSession s, TimelinePropertiesChangedEventArgs e) => OnUi(() => { _timeline = s.GetTimelineProperties(); return Task.CompletedTask; });

    private async Task RefreshAsync()
    {
        if (_manager is null)
        {
            ShowNothing();
            return;
        }

        var sessions = _manager.GetSessions().ToList();
        var preferred = _session is not null && sessions.Any(s => s.SourceAppUserModelId == _session.SourceAppUserModelId)
            ? sessions.First(s => s.SourceAppUserModelId == _session.SourceAppUserModelId)
            : _manager.GetCurrentSession() ?? sessions.FirstOrDefault();
        Attach(preferred);
        BuildSessionButtons(sessions);

        if (_session is null)
        {
            ShowNothing();
            return;
        }
        await RefreshMediaAsync();
        RefreshPlayback();
        _timeline = _session.GetTimelineProperties();
        UpdateProgress();
    }

    private void BuildSessionButtons(System.Collections.Generic.List<GlobalSystemMediaTransportControlsSession> sessions)
    {
        _sessions.Children.Clear();
        foreach (var s in sessions)
        {
            var b = Ui.Btn(AppName(s.SourceAppUserModelId), () =>
            {
                Attach(s);
                _ = RefreshAsync();
            }, primary: s == _session);
            b.Margin = new Thickness(0, 0, 6, 6);
            _sessions.Children.Add(b);
        }
        _hint.Text = sessions.Count == 0
            ? "Nenhum player tocando. Abra o YouTube Music e dê play: os controles aparecem aqui."
            : sessions.Count > 1 ? "Clique para escolher qual player controlar." : "";
    }

    private async Task RefreshMediaAsync()
    {
        if (_session is null) return;
        try
        {
            var props = await _session.TryGetMediaPropertiesAsync();
            _title.Text = string.IsNullOrWhiteSpace(props.Title) ? "Sem título" : props.Title;
            _artist.Text = string.Join(" · ", new[] { props.Artist, props.AlbumTitle }.Where(t => !string.IsNullOrWhiteSpace(t)));
            _source.Text = "Tocando em " + AppName(_session.SourceAppUserModelId);
            SetCover(props.Thumbnail is null ? null : await LoadThumbnailAsync(props.Thumbnail));
            Anim.FadeUp(_title, 0, 6);
        }
        catch (Exception)
        {
            // A sessão pode ter sido fechada no meio da leitura.
        }
    }

    private void RefreshPlayback()
    {
        if (_session is null) return;
        try
        {
            var info = _session.GetPlaybackInfo();
            _playing = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        }
        catch (Exception)
        {
            _playing = false;
        }
        _playPause.Content = _playing ? "" : "";
    }

    private void UpdateProgress()
    {
        if (_timeline is null || _timeline.EndTime <= TimeSpan.Zero)
        {
            _progress.Value = 0;
            _time.Text = "";
            return;
        }
        var position = _timeline.Position;
        if (_playing) position += DateTimeOffset.Now - _timeline.LastUpdatedTime;
        if (position > _timeline.EndTime) position = _timeline.EndTime;
        if (position < TimeSpan.Zero) position = TimeSpan.Zero;

        _progress.Value = position.TotalSeconds / _timeline.EndTime.TotalSeconds;
        _time.Text = $"{Format(position)} / {Format(_timeline.EndTime)}";
    }

    private void ShowNothing()
    {
        _title.Text = "Nada tocando";
        _artist.Text = "Dê play no YouTube Music (ou outro player) e ele aparece aqui.";
        _source.Text = "";
        _timeline = null;
        _playing = false;
        _playPause.Content = "";
        SetCover(null);
        UpdateProgress();
    }

    private async void Control(Func<GlobalSystemMediaTransportControlsSession, Windows.Foundation.IAsyncOperation<bool>> action, byte fallbackKey)
    {
        try
        {
            if (_session is not null && await action(_session)) return;
        }
        catch (Exception)
        {
            // Usa a tecla de mídia abaixo.
        }
        PressKey(fallbackKey);
    }

    // ---------- Capa ----------

    private void SetCover(ImageSource? image)
    {
        if (image is null)
        {
            _cover.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
            _cover.Child = new TextBlock
            {
                Text = "",
                FontFamily = (FontFamily)Application.Current.Resources["IconFont"],
                FontSize = 48,
                Foreground = Brushes.White,
                Opacity = 0.7,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }
        else
        {
            _cover.Child = null;
            _cover.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
            Anim.Pop(_cover);
        }
    }

    private static async Task<ImageSource?> LoadThumbnailAsync(Windows.Storage.Streams.IRandomAccessStreamReference reference)
    {
        try
        {
            using var stream = await reference.OpenReadAsync();
            using var input = stream.AsStreamForRead();
            var memory = new MemoryStream();
            await input.CopyToAsync(memory);
            memory.Position = 0;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = memory;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ---------- Auxiliares ----------

    private static Button RoundButton(string glyph, double size, bool primary, string tip, Action onClick)
    {
        var button = new Button
        {
            Content = glyph,
            FontFamily = (FontFamily)Application.Current.Resources["IconFont"],
            FontSize = size / 2.8,
            Width = size,
            Height = size,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 10, 0),
            ToolTip = tip,
            Style = (Style)Application.Current.Resources[primary ? "PrimaryButton" : "RoundButton"],
            Tag = new CornerRadius(size / 2),
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    private static string AppName(string id)
    {
        var lower = id.ToLowerInvariant();
        if (lower.Contains("chrome")) return "Google Chrome";
        if (lower.Contains("msedge") || lower.Contains("edge")) return "Microsoft Edge";
        if (lower.Contains("firefox")) return "Firefox";
        if (lower.Contains("opera")) return "Opera";
        if (lower.Contains("brave")) return "Brave";
        if (lower.Contains("spotify")) return "Spotify";
        if (lower.Contains("youtube")) return "YouTube Music";
        if (lower.Contains("zune") || lower.Contains("media")) return "Player do Windows";
        var name = Path.GetFileNameWithoutExtension(id.Split('!')[0]);
        return string.IsNullOrWhiteSpace(name) ? id : name;
    }

    private static string Format(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    // Teclas de mídia/volume do Windows (funcionam mesmo sem sessão de mídia).
    private const byte VkVolumeMute = 0xAD;
    private const byte VkVolumeDown = 0xAE;
    private const byte VkVolumeUp = 0xAF;
    private const byte VkMediaNext = 0xB0;
    private const byte VkMediaPrev = 0xB1;
    private const byte VkMediaPlayPause = 0xB3;
    private const uint KeyEventKeyUp = 0x0002;

    private static void PressKey(byte key, int times = 1)
    {
        for (var i = 0; i < times; i++)
        {
            keybd_event(key, 0, 0, UIntPtr.Zero);
            keybd_event(key, 0, KeyEventKeyUp, UIntPtr.Zero);
        }
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
}
