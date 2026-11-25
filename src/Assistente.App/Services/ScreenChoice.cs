using System;
using System.Collections.Generic;
using System.Linq;
using Forms = System.Windows.Forms;

namespace Assistente.App.Services;

/// <summary>Escolha de telas para dock e ilha: só a principal, todas ou uma tela específica.</summary>
public static class ScreenChoice
{
    /// <summary>Telas ligadas, numeradas da esquerda para a direita.</summary>
    public static List<(string Device, string Label, Forms.Screen Screen)> All() => Desktop.DesktopWidget.Screens();

    /// <summary>As telas que valem para a escolha (se a tela escolhida foi desligada, fica a principal).</summary>
    public static List<Forms.Screen> Targets(string? choice)
    {
        var all = All();
        var list = choice switch
        {
            "todas" => all.Select(s => s.Screen).ToList(),
            null or "" or "principal" => all.Where(s => s.Screen.Primary).Select(s => s.Screen).ToList(),
            var device => all.Where(s => s.Device == device).Select(s => s.Screen).ToList(),
        };
        if (list.Count == 0) list = all.Where(s => s.Screen.Primary).Select(s => s.Screen).ToList();
        if (list.Count == 0) list = Forms.Screen.AllScreens.Take(1).ToList();
        return list;
    }

    private static bool _hooked;
    private static readonly List<Action> Listeners = new();

    /// <summary>Avisa quando uma tela é ligada/desligada ou muda de resolução.</summary>
    public static void OnChanged(Action action)
    {
        Listeners.Add(action);
        if (_hooked) return;
        _hooked = true;
        System.Windows.Threading.DispatcherTimer? later = null;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (_, _) =>
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (later is null)
                {
                    later = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                    later.Tick += (_, _) =>
                    {
                        later!.Stop();
                        foreach (var l in Listeners.ToList()) l();
                    };
                }
                later.Stop();
                later.Start();
            }));
    }
}
