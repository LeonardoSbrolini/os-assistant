using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Assistente.App.Services;

/// <summary>
/// Deixa a barra de tarefas do Windows mais parecida com a do Mac usando só os ajustes
/// oficiais do próprio Windows (os mesmos de Configurações › Personalização › Barra de tarefas):
/// ícones no centro, sem pesquisa / visão de tarefas / widgets, e ocultar automaticamente.
/// Os valores originais ficam guardados para poder voltar como era.
/// </summary>
public static class TaskbarStyle
{
    private const string Advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string Search = @"Software\Microsoft\Windows\CurrentVersion\Search";

    public sealed record Option(string Id, string Key, string Name, int MacValue, int DefaultValue, string Label);

    public static readonly Option[] Options =
    {
        new("centro", Advanced, "TaskbarAl", 1, 1, "Ícones no centro (Windows 11)"),
        new("pesquisa", Search, "SearchboxTaskbarMode", 0, 1, "Esconder a pesquisa"),
        new("visao", Advanced, "ShowTaskViewButton", 0, 1, "Esconder o botão Visão de tarefas"),
        new("widgets", Advanced, "TaskbarDa", 0, 1, "Esconder os Widgets"),
        new("chat", Advanced, "TaskbarMn", 0, 1, "Esconder o Chat/Teams"),
    };

    /// <summary>Opção ligada no "jeito Mac"?</summary>
    public static bool IsMac(Option option) => Read(option) == option.MacValue;

    public static int Read(Option option)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(option.Key);
            return key?.GetValue(option.Name) is int value ? value : option.DefaultValue;
        }
        catch (Exception)
        {
            return option.DefaultValue;
        }
    }

    /// <summary>Liga/desliga uma opção. Retorna false se o Windows não deixou mudar.</summary>
    public static bool Set(AppSettings settings, Option option, bool mac)
    {
        try
        {
            settings.TaskbarOriginal ??= new Dictionary<string, int>();
            if (!settings.TaskbarOriginal.ContainsKey(option.Id))
            {
                settings.TaskbarOriginal[option.Id] = Read(option);
                settings.Save();
            }
            var value = mac ? option.MacValue : settings.TaskbarOriginal[option.Id] == option.MacValue ? option.DefaultValue : settings.TaskbarOriginal[option.Id];
            using var key = Registry.CurrentUser.CreateSubKey(option.Key, writable: true);
            key.SetValue(option.Name, value, RegistryValueKind.DWord);
            Notify();
            return Read(option) == value;
        }
        catch (Exception)
        {
            return false; // Algumas versões do Windows bloqueiam (ex.: Widgets).
        }
    }

    /// <summary>Liga todas as opções do visual Mac.</summary>
    public static int ApplyMacLook(AppSettings settings)
    {
        var failed = 0;
        foreach (var option in Options)
            if (!Set(settings, option, true)) failed++;
        return failed;
    }

    /// <summary>Volta tudo para como estava antes do Assistente mexer.</summary>
    public static void Restore(AppSettings settings)
    {
        if (settings.TaskbarOriginal is { } original)
        {
            foreach (var option in Options)
            {
                if (!original.TryGetValue(option.Id, out var value)) continue;
                try
                {
                    using var key = Registry.CurrentUser.CreateSubKey(option.Key, writable: true);
                    key.SetValue(option.Name, value, RegistryValueKind.DWord);
                }
                catch (Exception)
                {
                    // Ignora a que não der.
                }
            }
            settings.TaskbarOriginal = null;
        }
        if (settings.TaskbarAutoHideOriginal is { } autoHide)
        {
            try { Docking.Native.TaskbarAutoHide = autoHide; } catch (Exception) { /* sem barra */ }
            settings.TaskbarAutoHideOriginal = null;
        }
        settings.Save();
        Notify();
    }

    /// <summary>"Ocultar automaticamente" da barra (como o Dock do Mac escondido).</summary>
    public static bool AutoHide
    {
        get
        {
            try { return Docking.Native.TaskbarAutoHide; }
            catch (Exception) { return false; }
        }
    }

    public static void SetAutoHide(AppSettings settings, bool value)
    {
        try
        {
            settings.TaskbarAutoHideOriginal ??= Docking.Native.TaskbarAutoHide;
            settings.Save();
            Docking.Native.TaskbarAutoHide = value;
        }
        catch (Exception)
        {
            // Sem barra de tarefas.
        }
    }

    /// <summary>Reinicia o Explorer (a barra pisca e volta já com as mudanças).</summary>
    public static void RestartExplorer()
    {
        try
        {
            foreach (var p in Process.GetProcessesByName("explorer"))
            {
                using (p)
                {
                    try { p.Kill(); } catch (Exception) { /* sem permissão */ }
                }
            }
        }
        finally
        {
            // O Windows normalmente reabre sozinho; garante caso não reabra.
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (Process.GetProcessesByName("explorer").Length == 0)
                    Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
            };
            timer.Start();
        }
    }

    /// <summary>Avisa o Windows que as configurações da barra mudaram.</summary>
    private static void Notify()
    {
        try
        {
            SendMessageTimeout((IntPtr)0xFFFF, 0x001A /* WM_SETTINGCHANGE */, IntPtr.Zero, "TraySettings", 0x0002, 300, out _);
        }
        catch (Exception)
        {
            // Melhor esforço.
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr w, string l, uint flags, uint timeout, out IntPtr result);
}
