using System;
using System.Windows;
using System.Windows.Threading;
using Assistente.App.Services;

namespace Assistente.App.Widgets;

/// <summary>
/// Liga os mini widgets ao avatar: aparecem com o painel fechado, somem com ele aberto
/// e acompanham a posição da alça.
/// </summary>
public static class WidgetManager
{
    private static MusicWidget? _music;
    private static TimeWidget? _time;
    private static readonly DispatcherTimer Check = new() { Interval = TimeSpan.FromSeconds(15) };

    public static void Start(MainWindow main)
    {
        if (_music is not null) return;
        _music = new MusicWidget();
        _time = new TimeWidget();

        main.HandleLayoutChanged += Refresh;
        MediaService.Instance.Changed += Refresh;
        Check.Tick += (_, _) => Refresh(); // esconde o player depois de muito tempo pausado
        Check.Start();
        MediaService.Instance.Start();
        Refresh();
    }

    private static bool _suppressed;

    /// <summary>Escondidos junto com o avatar (ex.: navegador em tela cheia por cima).</summary>
    public static bool Suppressed
    {
        get => _suppressed;
        set
        {
            if (_suppressed == value) return;
            _suppressed = value;
            Refresh();
        }
    }

    /// <summary>Retângulo (DIPs) que junta o avatar com os widgets que podem aparecer.</summary>
    public static Rect UnionWith(Rect handle)
    {
        foreach (var w in new MiniWidget?[] { _music, _time })
        {
            if (w is null || !w.HasSomething || w.ActualWidth <= 0) continue;
            handle.Union(new Rect(w.Left, w.Top, w.ActualWidth, w.ActualHeight));
        }
        return handle;
    }

    /// <summary>Algum widget aparecendo agora (o avatar não se esconde nesse caso).</summary>
    public static bool AnyShown => _music?.IsVisible == true || _time?.IsVisible == true;

    public static void Refresh()
    {
        var main = MainWindow.Current;
        if (main is null || _music is null || _time is null) return;
        var visible = !main.IsExpanded && main.IsVisible && !_suppressed;

        _music.SetShown(visible && main.Settings.MusicWidget && _music.HasSomething);
        _time.SetShown(visible && main.Settings.TimeWidget && _time.HasSomething);
        if (_music.IsVisible) _music.Place();
        if (_time.IsVisible) _time.Place();
    }
}
