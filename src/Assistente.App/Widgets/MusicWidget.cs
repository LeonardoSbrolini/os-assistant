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
        Width = 24,
        Height = 24,
        CornerRadius = new CornerRadius(6),
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 8, 0),
    };
    private readonly TextBlock _title = new()
    {
        FontSize = 12,
        FontWeight = FontWeights.SemiBold,
        MaxWidth = 170,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };
    private readonly TextBlock _artist = new()
    {
        FontSize = 11.5,
        MaxWidth = 120,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };
    private readonly Button _playPause;
    private readonly StackPanel _info;
    private ImageSource? _lastCover;
    private string _lastTitle = "";

    public MusicWidget() : base(WidgetSide.After)
    {
        _artist.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        _playPause = IconButton("", "Tocar / pausar", Media.TogglePlayPause, primary: true, size: 26);

        // Uma linha só (título · artista), para caber na altura de uma aba.
        _artist.Margin = new Thickness(6, 0, 0, 0);
        var text = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        text.Children.Add(_title);
        text.Children.Add(_artist);

        // Clicar na capa ou no nome abre a aba Música.
        var info = new StackPanel { Orientation = Orientation.Horizontal, Cursor = Cursors.Hand, Background = Brushes.Transparent };
        info.Children.Add(_cover);
        info.Children.Add(text);
        _info = info;
        Tip(info, "Abrir a aba Música");
        info.MouseLeftButtonUp += (_, _) => MainWindow.Current?.ShowTool("Tocando agora");

        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(info);
        row.Children.Add(IconButton("", "Anterior", Media.Previous));
        row.Children.Add(_playPause);
        row.Children.Add(IconButton("", "Próxima", Media.Next));
        var close = IconButton("\uE711", "Fechar o player (volta quando tocar outra música)", Dismiss, size: 20);
        close.Margin = new Thickness(6, 0, 0, 0);
        close.Opacity = 0.75;
        row.Children.Add(close);
        Pill.Child = row;
        Pill.Padding = new Thickness(5, 0, 5, 0);

        SetCover(null);
        Media.Changed += Refresh;
        Refresh();
    }

    /// <summary>Música que o usuário fechou: o player só volta quando mudar de música.</summary>
    private string? _dismissed;

    private string CurrentKey => Media.AppName + "|" + Media.Title;

    public override bool HasSomething =>
        Media.HasSession && _dismissed != CurrentKey &&
        (Media.IsPlaying || DateTime.Now - Media.LastPlayingAt < HideAfterPause);

    private void Dismiss()
    {
        _dismissed = CurrentKey;
        WidgetManager.Refresh();
    }

    private void Refresh()
    {
        if (!Media.HasSession) return;
        _title.Text = Media.Title;
        _artist.Text = Media.Artist.Length > 0 ? Media.Artist : Media.AppName;
        Tip(_info, $"{Media.Title}\n{Media.Artist}\nTocando em {Media.AppName}\n(clique para abrir a aba Música)");
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
