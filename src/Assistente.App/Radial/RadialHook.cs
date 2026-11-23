using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace Assistente.App.Radial;

/// <summary>
/// Escuta o mouse do Windows inteiro: se você está arrastando um arquivo/app no Explorer ou
/// na área de trabalho (botão esquerdo segurado) e clica com o botão direito, abre o menu em
/// círculo em volta do cursor. O clique direito é "engolido" para o Explorer não abrir o menu dele.
/// </summary>
internal static class RadialHook
{
    private const int WhMouseLl = 14;
    private const int WmMouseMove = 0x0200, WmLButtonDown = 0x0201, WmLButtonUp = 0x0202, WmRButtonDown = 0x0204, WmRButtonUp = 0x0205;
    private const int DragThreshold = 8;

    private static IntPtr _hook = IntPtr.Zero;
    private static LowLevelMouseProc? _proc; // mantém o delegate vivo
    private static bool _leftDown;
    private static bool _dragging;
    private static POINT _downAt;
    private static bool _swallowRightUp;
    private static bool _sourceIsExplorer;

    public static void Apply(bool enabled)
    {
        if (enabled && _hook == IntPtr.Zero)
        {
            _proc = Callback;
            using var module = Process.GetCurrentProcess().MainModule;
            _hook = SetWindowsHookEx(WhMouseLl, _proc, GetModuleHandle(module?.ModuleName), 0);
        }
        else if (!enabled && _hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private static IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            try
            {
                var msg = wParam.ToInt32();
                var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                switch (msg)
                {
                    case WmLButtonDown:
                        _leftDown = true;
                        _dragging = false;
                        _downAt = data.pt;
                        _sourceIsExplorer = IsExplorerAt(data.pt);
                        break;
                    case WmMouseMove when _leftDown && !_dragging:
                        if (Math.Abs(data.pt.X - _downAt.X) > DragThreshold || Math.Abs(data.pt.Y - _downAt.Y) > DragThreshold) _dragging = true;
                        break;
                    case WmLButtonUp:
                        _leftDown = false;
                        _dragging = false;
                        if (RadialMenu.IsOpen) Application.Current?.Dispatcher.BeginInvoke(new Action(RadialMenu.LeftReleased), DispatcherPriority.Background);
                        break;
                    case WmRButtonDown when _leftDown && _dragging && _sourceIsExplorer && !RadialMenu.IsOpen:
                        var pt = data.pt;
                        _swallowRightUp = true;
                        Application.Current?.Dispatcher.BeginInvoke(new Action(() => RadialMenu.ShowAt(pt.X, pt.Y)));
                        return new IntPtr(1);
                    case WmRButtonDown when RadialMenu.IsOpen:
                        _swallowRightUp = true;
                        return new IntPtr(1);
                    case WmRButtonUp when _swallowRightUp:
                        _swallowRightUp = false;
                        return new IntPtr(1);
                }
            }
            catch (Exception)
            {
                // Nunca atrapalha o mouse.
            }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private static readonly string[] ExplorerClasses = { "CabinetWClass", "ExploreWClass", "Progman", "WorkerW" };

    /// <summary>Só no Explorer/área de trabalho (para não atrapalhar jogos e outros apps).</summary>
    private static bool IsExplorerAt(POINT pt)
    {
        var hwnd = WindowFromPoint(pt);
        if (hwnd == IntPtr.Zero) return false;
        var root = GetAncestor(hwnd, 2 /* GA_ROOT */);
        var cls = new System.Text.StringBuilder(64);
        GetClassName(root, cls, cls.Capacity);
        return Array.IndexOf(ExplorerClasses, cls.ToString()) >= 0;
    }

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData, flags, time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? lpModuleName);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);
}
