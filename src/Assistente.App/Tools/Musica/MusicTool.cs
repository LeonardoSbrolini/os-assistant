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
using Assistente.App.Services;

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

    private static MediaService Media => MediaService.Instance;
    private string _lastTitle = "";
    private ImageSource? _lastCover;

    public MusicView()
    {
        _artist.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        SetCover(null);

        _playPause = RoundButton("", 56, primary: true, "Tocar / pausar", Media.TogglePlayPause);
        var prev = RoundButton("", 42, false, "Anterior", Media.Previous);
        var next = RoundButton("", 42, false, "Próxima", Media.Next);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        controls.Children.Add(prev);
        controls.Children.Add(_playPause);
        controls.Children.Add(next);

        var volume = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        volume.Children.Add(RoundButton("", 34, false, "Mudo", MediaService.VolumeMute));
        volume.Children.Add(RoundButton("", 34, false, "Diminuir volume", MediaService.VolumeDown));
        volume.Children.Add(RoundButton("", 34, false, "Aumentar volume", MediaService.VolumeUp));

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
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                Media.Changed += Refresh;
                Media.Start();
                Refresh();
                _progressTimer.Start();
            }
            else
            {
                Media.Changed -= Refresh;
                _progressTimer.Stop();
            }
        };
    }

    private void Refresh()
    {
        BuildSessionButtons();
        if (!Media.HasSession)
        {
            ShowNothing();
            return;
        }
        _title.Text = Media.Title;
        _artist.Text = string.Join(" · ", new[] { Media.Artist, Media.Album }.Where(t => !string.IsNullOrWhiteSpace(t)));
        _source.Text = "Tocando em " + Media.AppName;
        if (_lastTitle != Media.Title) Anim.FadeUp(_title, 0, 6);
        _lastTitle = Media.Title;
        if (!ReferenceEquals(_lastCover, Media.Cover)) SetCover(Media.Cover);
        _lastCover = Media.Cover;
        _playPause.Content = Media.IsPlaying ? "\uE769" : "\uE768";
        UpdateProgress();
    }

    private void BuildSessionButtons()
    {
        _sessions.Children.Clear();
        foreach (var s in Media.Sessions)
        {
            var session = s;
            var b = Ui.Btn(MediaService.FriendlyAppName(s.SourceAppUserModelId), () => Media.Select(session),
                primary: ReferenceEquals(s, Media.Session));
            b.Margin = new Thickness(0, 0, 6, 6);
            _sessions.Children.Add(b);
        }
        _hint.Text = !Media.Available
            ? "O controle de mídia do Windows não está disponível neste computador. Os botões usam as teclas de mídia."
            : Media.Sessions.Count == 0
                ? "Nenhum player tocando. Abra o YouTube Music e dê play: os controles aparecem aqui."
                : Media.Sessions.Count > 1 ? "Clique para escolher qual player controlar." : "";
    }

    private void UpdateProgress()
    {
        var (position, duration) = Media.GetTimeline();
        if (duration <= TimeSpan.Zero)
        {
            _progress.Value = 0;
            _time.Text = "";
            return;
        }
        _progress.Value = position.TotalSeconds / duration.TotalSeconds;
        _time.Text = $"{Format(position)} / {Format(duration)}";
    }

    private void ShowNothing()
    {
        _title.Text = "Nada tocando";
        _artist.Text = "Dê play no YouTube Music (ou outro player) e ele aparece aqui.";
        _source.Text = "";
        _lastTitle = "";
        _lastCover = null;
        _playPause.Content = "\uE768";
        SetCover(null);
        UpdateProgress();
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

    private static string Format(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
}
