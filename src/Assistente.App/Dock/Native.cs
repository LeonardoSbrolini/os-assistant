using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Assistente.App.Docking;

/// <summary>Janela aberta de outro programa (como as que aparecem no Alt+Tab).</summary>
public sealed record OpenWindow(IntPtr Handle, string Title, string ExePath, bool Minimized);

/// <summary>Chamadas do Windows usadas pelo dock: listar janelas, ícones, ativar, barra de tarefas.</summary>
internal static class Native
{
    // ---------- Janelas abertas ----------

    private static readonly HashSet<string> IgnoredClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow",
        "NotifyIconOverflowWindow", "XamlExplorerHostIslandWindow",
    };

    public static List<OpenWindow> GetOpenWindows()
    {
        var list = new List<OpenWindow>();
        var ownPid = (uint)Environment.ProcessId;
        EnumWindows((hwnd, _) =>
        {
            try
            {
                if (!IsWindowVisible(hwnd) || GetWindow(hwnd, GwOwner) != IntPtr.Zero) return true;
                var ex = GetWindowLong(hwnd, GwlExStyle);
                if ((ex & WsExToolWindow) != 0 && (ex & WsExAppWindow) == 0) return true;
                if (IsCloaked(hwnd)) return true;

                var length = GetWindowTextLength(hwnd);
                if (length == 0) return true;
                var title = new StringBuilder(length + 1);
                GetWindowText(hwnd, title, title.Capacity);

                var cls = new StringBuilder(256);
                GetClassName(hwnd, cls, cls.Capacity);
                if (IgnoredClasses.Contains(cls.ToString())) return true;

                GetWindowThreadProcessId(hwnd, out var pid);
                if (pid == ownPid) return true;
                var exe = GetProcessPath(pid);
                if (exe is null) return true;

                list.Add(new OpenWindow(hwnd, title.ToString(), exe, IsIconic(hwnd)));
            }
            catch (Exception)
            {
                // Janela fechou no meio da leitura.
            }
            return true;
        }, IntPtr.Zero);
        return list;
    }

    private static bool IsCloaked(IntPtr hwnd)
    {
        try
        {
            return DwmGetWindowAttribute(hwnd, DwmwaCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static string? GetProcessPath(uint pid)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var buffer = new StringBuilder(1024);
            var size = buffer.Capacity;
            return QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString() : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    public static uint GetWindowProcessId(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        return pid;
    }

    public static IntPtr Foreground => GetForegroundWindow();

    public static bool IsMinimized(IntPtr hwnd) => IsIconic(hwnd);

    public static void Activate(IntPtr hwnd)
    {
        if (IsIconic(hwnd)) ShowWindow(hwnd, SwRestore);
        SetForegroundWindow(hwnd);
    }

    public static void Minimize(IntPtr hwnd) => ShowWindow(hwnd, SwMinimize);

    public static void Close(IntPtr hwnd) => PostMessage(hwnd, WmClose, IntPtr.Zero, IntPtr.Zero);

    /// <summary>A janela em primeiro plano ocupa a tela inteira (jogo, vídeo em tela cheia...)?</summary>
    public static bool IsForegroundFullscreen()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;
        var cls = new StringBuilder(256);
        GetClassName(hwnd, cls, cls.Capacity);
        if (IgnoredClasses.Contains(cls.ToString())) return false;
        if (!GetWindowRect(hwnd, out var r)) return false;
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return false;
        var m = info.Monitor;
        return r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
    }

    // ---------- Ícones ----------

    private static readonly Dictionary<string, ImageSource?> IconCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Ícone grande do programa ou atalho (com cache).</summary>
    public static ImageSource? GetIcon(string path, int size = 96)
    {
        if (IconCache.TryGetValue(path, out var cached)) return cached;
        ImageSource? image = null;
        try
        {
            if (SHDefExtractIcon(path, 0, 0, out var large, out var small, (uint)(size | (16 << 16))) == 0 && large != IntPtr.Zero)
            {
                image = FromHIcon(large);
                DestroyIcon(large);
                if (small != IntPtr.Zero) DestroyIcon(small);
            }
            if (image is null)
            {
                var info = new ShFileInfo();
                if (SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), ShgfiIcon | ShgfiLargeIcon) != IntPtr.Zero &&
                    info.Icon != IntPtr.Zero)
                {
                    image = FromHIcon(info.Icon);
                    DestroyIcon(info.Icon);
                }
            }
        }
        catch (Exception)
        {
            image = null;
        }
        IconCache[path] = image;
        return image;
    }

    /// <summary>Ícone da própria janela (para apps da Microsoft Store, que rodam todos no mesmo processo).</summary>
    public static ImageSource? GetWindowIcon(IntPtr hwnd)
    {
        try
        {
            SendMessageTimeout(hwnd, WmGetIcon, (IntPtr)1, IntPtr.Zero, SmtoAbortIfHung, 100, out var icon);
            if (icon == IntPtr.Zero) icon = GetClassLongPtr(hwnd, GclpHIcon);
            return icon == IntPtr.Zero ? null : FromHIcon(icon);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static ImageSource? FromHIcon(IntPtr icon)
    {
        var source = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        source.Freeze();
        return source;
    }

    /// <summary>Destino de um atalho .lnk (ou o próprio caminho).</summary>
    private static readonly Dictionary<string, string> TargetCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> NameCache = new(StringComparer.OrdinalIgnoreCase);

    public static string ResolveTarget(string path)
    {
        if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return path;
        if (TargetCache.TryGetValue(path, out var cached)) return cached;
        return TargetCache[path] = ReadShortcut(path);
    }

    private static string ReadShortcut(string path)
    {
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null) return path;
            dynamic shell = Activator.CreateInstance(type)!;
            var target = (string)shell.CreateShortcut(path).TargetPath;
            return string.IsNullOrWhiteSpace(target) ? path : target;
        }
        catch (Exception)
        {
            return path;
        }
    }

    public static string DisplayName(string path)
    {
        if (NameCache.TryGetValue(path, out var cached)) return cached;
        return NameCache[path] = ReadDisplayName(path);
    }

    private static string ReadDisplayName(string path)
    {
        try
        {
            var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(ResolveTarget(path));
            if (!string.IsNullOrWhiteSpace(info.FileDescription) && !path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                return info.FileDescription.Trim();
        }
        catch (Exception)
        {
            // Usa o nome do arquivo.
        }
        return Path.GetFileNameWithoutExtension(path);
    }

    // ---------- Barra de tarefas do Windows ----------

    /// <summary>Barra de tarefas do Windows está em "ocultar automaticamente"?</summary>
    public static bool TaskbarAutoHide
    {
        get
        {
            var data = new AppBarData { Size = Marshal.SizeOf<AppBarData>() };
            return ((int)SHAppBarMessage(AbmGetState, ref data) & AbsAutoHide) != 0;
        }
        set
        {
            var data = new AppBarData
            {
                Size = Marshal.SizeOf<AppBarData>(),
                Hwnd = FindWindow("Shell_TrayWnd", null),
                Param = value ? AbsAutoHide : AbsAlwaysOnTop,
            };
            SHAppBarMessage(AbmSetState, ref data);
        }
    }

    /// <summary>Esconde (ou mostra) de vez as barras de tarefas do Windows, inclusive dos outros monitores.</summary>
    public static void SetTaskbarVisible(bool visible)
    {
        foreach (var bar in TaskbarWindows())
        {
            if (IsWindowVisible(bar) != visible) ShowWindow(bar, visible ? SwShowNa : SwHide);
        }
    }

    /// <summary>Alguma barra do Windows voltou a aparecer (o Explorer às vezes mostra de novo)?</summary>
    public static bool AnyTaskbarVisible()
    {
        foreach (var bar in TaskbarWindows())
            if (IsWindowVisible(bar)) return true;
        return false;
    }

    private static List<IntPtr> TaskbarWindows()
    {
        var list = new List<IntPtr>();
        var main = FindWindow("Shell_TrayWnd", null);
        if (main != IntPtr.Zero) list.Add(main);
        var other = IntPtr.Zero;
        while ((other = FindWindowEx(IntPtr.Zero, other, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero) list.Add(other);
        return list;
    }

    /// <summary>Menu Iniciar / pesquisa aberto (não esconder a barra nesse momento).</summary>
    public static bool IsShellUiForeground()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;
        var cls = new StringBuilder(256);
        GetClassName(hwnd, cls, cls.Capacity);
        var name = cls.ToString();
        if (name is "Windows.UI.Core.CoreWindow" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "XamlExplorerHostIslandWindow")
            return true;
        var exe = GetProcessPath(GetWindowProcessId(hwnd)) ?? "";
        var file = Path.GetFileName(exe);
        return file.Equals("StartMenuExperienceHost.exe", StringComparison.OrdinalIgnoreCase) ||
               file.Equals("SearchHost.exe", StringComparison.OrdinalIgnoreCase) ||
               file.Equals("SearchApp.exe", StringComparison.OrdinalIgnoreCase) ||
               file.Equals("ShellExperienceHost.exe", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Posição do mouse em pixels da tela.</summary>
    public static (int X, int Y) CursorPosition()
    {
        GetCursorPos(out var p);
        return (p.X, p.Y);
    }

    public static (int Width, int Height) PrimaryScreenPixels() => (GetSystemMetrics(SmCxScreen), GetSystemMetrics(SmCyScreen));

    /// <summary>
    /// Retângulo (pixels) da janela em primeiro plano de outro programa, se for uma janela "normal"
    /// visível (não a área de trabalho, não minimizada).
    /// </summary>
    public static Rect? ForegroundAppRect()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero || IsIconic(hwnd) || !IsWindowVisible(hwnd)) return null;
        if (GetWindowProcessId(hwnd) == (uint)Environment.ProcessId) return null;
        var cls = new StringBuilder(256);
        GetClassName(hwnd, cls, cls.Capacity);
        if (IgnoredClasses.Contains(cls.ToString())) return null;
        if (!GetWindowRect(hwnd, out var r)) return null;
        return new Rect(r.Left, r.Top, Math.Max(0, r.Right - r.Left), Math.Max(0, r.Bottom - r.Top));
    }

    /// <summary>Abre o menu Iniciar (como apertar a tecla Windows).</summary>
    public static void OpenStartMenu()
    {
        keybd_event(VkLWin, 0, 0, UIntPtr.Zero);
        keybd_event(VkLWin, 0, KeyEventKeyUp, UIntPtr.Zero);
    }

    // ---------- Reservar espaço na tela (AppBar) ----------

    /// <summary>
    /// Reserva uma faixa na parte de baixo da tela principal (janelas maximizadas não ficam
    /// atrás do dock). Retorna a faixa obtida em pixels.
    /// </summary>
    public static Rect? RegisterBottomBar(IntPtr hwnd, int heightPx, uint callbackMessage, bool alreadyRegistered)
    {
        var data = new AppBarData { Size = Marshal.SizeOf<AppBarData>(), Hwnd = hwnd, CallbackMessage = callbackMessage };
        if (!alreadyRegistered && SHAppBarMessage(AbmNew, ref data) == IntPtr.Zero) return null;

        var screenW = GetSystemMetrics(SmCxScreen);
        var screenH = GetSystemMetrics(SmCyScreen);
        data.Edge = AbeBottom;
        data.Rc = new NativeRect { Left = 0, Right = screenW, Top = screenH - heightPx, Bottom = screenH };
        SHAppBarMessage(AbmQueryPos, ref data);
        data.Rc.Top = data.Rc.Bottom - heightPx;
        SHAppBarMessage(AbmSetPos, ref data);
        return new Rect(data.Rc.Left, data.Rc.Top, data.Rc.Right - data.Rc.Left, data.Rc.Bottom - data.Rc.Top);
    }

    public static void UnregisterBar(IntPtr hwnd)
    {
        var data = new AppBarData { Size = Marshal.SizeOf<AppBarData>(), Hwnd = hwnd };
        SHAppBarMessage(AbmRemove, ref data);
    }

    // ---------- P/Invoke ----------

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    private const int GwOwner = 4;
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExAppWindow = 0x00040000;
    private const int DwmwaCloaked = 14;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int SwRestore = 9;
    private const int SwMinimize = 6;
    private const int SwHide = 0;
    private const int SwShowNa = 8;
    private const uint WmClose = 0x0010;
    private const uint WmGetIcon = 0x007F;
    private const int GclpHIcon = -14;
    private const uint SmtoAbortIfHung = 0x0002;
    private const uint ShgfiIcon = 0x100;
    private const uint ShgfiLargeIcon = 0x0;
    private const uint AbmNew = 0, AbmRemove = 1, AbmQueryPos = 2, AbmSetPos = 3, AbmGetState = 4, AbmSetState = 10;
    private const int AbsAutoHide = 1, AbsAlwaysOnTop = 2;
    private const uint AbeBottom = 3;
    private const int SmCxScreen = 0, SmCyScreen = 1;
    private const byte VkLWin = 0x5B;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        public int Size;
        public IntPtr Hwnd;
        public uint CallbackMessage;
        public uint Edge;
        public NativeRect Rc;
        public IntPtr Param;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public IntPtr Icon;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string cls, string? title);
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string? title);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")] private static extern IntPtr GetClassLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHDefExtractIcon(string file, int index, uint flags, out IntPtr large, out IntPtr small, uint sizes);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref ShFileInfo info, uint size, uint flags);
    [DllImport("shell32.dll")] private static extern IntPtr SHAppBarMessage(uint msg, ref AppBarData data);
}
