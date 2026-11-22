using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Assistente.App.Services;
using Assistente.App.Tools;

namespace Assistente.App.Widgets;

/// <summary>Mini player à direita do avatar: capa, música e anterior / tocar-pausar / próxima.</summary>
public sealed class MusicWidget : MiniWidget
{
    /// <summary>Depois de tanto tempo pausado, o widget some sozinho.</summary>
    private static readonly TimeSpan HideAfterPause = TimeSpan.FromMinutes(10);

    private static MediaService Media => MediaService.Instance;

    private readonly Border _cover = new()
    {
        Width = 30,
        Height = 30,
        CornerRadius = new CornerRadius(15),
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 8, 0),
    };
    private readonly TextBlock _title = new()
    {
        FontSize = 12,
        FontWeight = FontWeights.SemiBold,
        MaxWidth = 150,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };
    private readonly TextBlock _artist = new()
    {
        FontSize = 10.5,
        MaxWidth = 150,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };
    private readonly Button _playPause;
    private ImageSource? _lastCover;
    private string _lastTitle = "";

    public MusicWidget() : base(WidgetSide.After)
    {
        _artist.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        _playPause = IconButton("", "Tocar / pausar", Media.TogglePlayPause, primary: true, size: 30);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), MinWidth = 60 };
        text.Children.Add(_title);
        text.Children.Add(_artist);

        // Clicar na capa ou no nome abre a aba Música.
        var info = new StackPanel { Orientation = Orientation.Horizontal, Cursor = Cursors.Hand, Background = Brushes.Transparent };
        info.Children.Add(_cover);
        info.Children.Add(text);
        info.ToolTip = "Abrir a aba Música";
        info.MouseLeftButtonUp += (_, _) => MainWindow.Current?.ShowTool("Tocando agora");

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(info);
        row.Children.Add(IconButton("", "Anterior", Media.Previous));
        row.Children.Add(_playPause);
        row.Children.Add(IconButton("", "Próxima", Media.Next));
        Pill.Child = row;
        Pill.Padding = new Thickness(4, 4, 5, 4);

        SetCover(null);
        Media.Changed += Refresh;
        Refresh();
    }

    public override bool HasSomething =>
        Media.HasSession && (Media.IsPlaying || DateTime.Now - Media.LastPlayingAt < HideAfterPause);

    private void Refresh()
    {
        if (!Media.HasSession) return;
        _title.Text = Media.Title;
        _artist.Text = Media.Artist.Length > 0 ? Media.Artist : Media.AppName;
        ToolTip = $"{Media.Title}\n{Media.Artist}\nTocando em {Media.AppName}";
        _playPause.Content = Media.IsPlaying ? "" : "";
        if (!ReferenceEquals(_lastCover, Media.Cover))
        {
            _lastCover = Media.Cover;
            SetCover(Media.Cover);
        }
        if (_lastTitle != Media.Title && IsVisible) Anim.FadeUp(_title, 0, 4);
        _lastTitle = Media.Title;
    }

    private void SetCover(ImageSource? image)
    {
        if (image is null)
        {
            _cover.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            _cover.Child = new TextBlock
            {
                Text = "",
                FontFamily = (FontFamily)Application.Current.Resources["IconFont"],
                FontSize = 13,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }
        else
        {
            _cover.Child = null;
            _cover.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
            if (IsVisible) Anim.Pop(_cover);
        }
    }
}
