using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Assistente.App.Desktop;

/// <summary>
/// Mostra/esconde os ícones da área de trabalho, igual ao "Exibir › Mostrar ícones da área de
/// trabalho" do botão direito (o Windows guarda a escolha sozinho).
/// </summary>
public static class DesktopIcons
{
    private const uint WmCommand = 0x0111;
    private const int ToggleIconsCommand = 0x7402;

    /// <summary>Janela que contém os ícones (SHELLDLL_DefView), dentro do Progman ou de um WorkerW.</summary>
    private static IntPtr DefView()
    {
        var progman = FindWindow("Progman", null);
        var view = progman == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (view != IntPtr.Zero) return view;

        // Com papel de parede animado/slideshow o Windows muda os ícones para um WorkerW.
        EnumWindows((hwnd, _) =>
        {
            var cls = new StringBuilder(64);
            GetClassName(hwnd, cls, cls.Capacity);
            if (cls.ToString() != "WorkerW") return true;
            var found = FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (found == IntPtr.Zero) return true;
            view = found;
            return false;
        }, IntPtr.Zero);
        return view;
    }

    /// <summary>Os ícones estão à vista agora? (null = não deu para saber)</summary>
    public static bool? AreVisible()
    {
        var view = DefView();
        if (view == IntPtr.Zero) return null;
        var list = FindWindowEx(view, IntPtr.Zero, "SysListView32", null);
        return list == IntPtr.Zero ? null : IsWindowVisible(list);
    }

    public static void SetVisible(bool visible)
    {
        try
        {
            if (AreVisible() is not { } now || now == visible) return;
            SendMessage(DefView(), WmCommand, (IntPtr)ToggleIconsCommand, IntPtr.Zero);
        }
        catch (Exception)
        {
            // Melhor esforço: se o Explorer estiver reiniciando, tenta na próxima vez.
        }
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string cls, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string? title);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
}
