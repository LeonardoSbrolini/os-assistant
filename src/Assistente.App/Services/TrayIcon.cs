using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Assistente.App.Services;

/// <summary>Ícone na bandeja do sistema com o menu de opções.</summary>
public sealed class TrayIcon : IDisposable
{
    private static TrayIcon? _current;
    private readonly NotifyIcon _icon;

    public TrayIcon(MainWindow window, AppSettings settings)
    {
        var menu = new ContextMenuStrip();

        menu.Items.Add(new ToolStripMenuItem("Abrir painel", null, (_, _) => window.ExpandPinned()));
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add(new ToolStripMenuItem("Configurações…", null, (_, _) => SettingsWindow.Open()));

        var startup = new ToolStripMenuItem("Iniciar com o Windows") { CheckOnClick = true };
        startup.CheckedChanged += (_, _) => StartupManager.SetEnabled(startup.Checked);
        menu.Opening += (_, _) => startup.Checked = StartupManager.IsEnabled; // reflete mudanças feitas nas configurações
        menu.Items.Add(startup);

        var dock = new ToolStripMenuItem("Dock estilo Mac") { CheckOnClick = true };
        dock.Click += (_, _) =>
        {
            settings.DockEnabled = dock.Checked;
            settings.Save();
            Docking.DockWindow.Apply(settings);
        };
        menu.Opening += (_, _) => dock.Checked = settings.DockEnabled;
        menu.Items.Add(dock);


        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Verificar atualizações", null,
            async (_, _) => await UpdateService.Instance.CheckAsync(manual: true)));
        menu.Items.Add(new ToolStripMenuItem($"Versão {UpdateService.CurrentVersionText}") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Sair", null, (_, _) => System.Windows.Application.Current.Shutdown()));

        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "Assistente",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => window.ExpandPinned();
        _current = this;
    }

    /// <summary>Mostra um aviso do Windows (balão/notificação) a partir do ícone da bandeja.</summary>
    public static void Notify(string title, string text)
    {
        _current?._icon.ShowBalloonTip(5000, title, text, ToolTipIcon.Info);
    }

    private static Icon LoadIcon()
    {
        try
        {
            if (Environment.ProcessPath is { } path && Icon.ExtractAssociatedIcon(path) is { } icon)
                return icon;
        }
        catch (Exception)
        {
            // Usa o ícone padrão abaixo.
        }
        return SystemIcons.Application;
    }

    public void Dispose()
    {
        _current = null;
        _icon.Visible = false;
        _icon.Dispose();
    }
}
