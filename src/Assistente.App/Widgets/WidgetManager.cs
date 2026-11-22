using System;
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

    public static void Refresh()
    {
        var main = MainWindow.Current;
        if (main is null || _music is null || _time is null) return;
        var visible = !main.IsExpanded && main.IsVisible;

        _music.SetShown(visible && main.Settings.MusicWidget && _music.HasSomething);
        _time.SetShown(visible && main.Settings.TimeWidget && _time.HasSomething);
        if (_music.IsVisible) _music.Place();
        if (_time.IsVisible) _time.Place();
    }
}
