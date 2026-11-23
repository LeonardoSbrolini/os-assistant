using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Assistente.App.Radial;

/// <summary>
/// Onde o arquivo foi solto: a pasta da janela do Explorer (ou a área de trabalho) que estava
/// embaixo do cursor. Os arquivos novos vão para lá e, quando dá, aparecem exatamente no ponto
/// em que você soltou.
/// </summary>
internal sealed class DropTarget
{
    /// <summary>Pasta onde salvar; null = ao lado do arquivo original.</summary>
    public string? Folder { get; private init; }

    private readonly int _x, _y;
    private readonly bool _desktop;
    private readonly IntPtr _root;

    private DropTarget(int x, int y, bool desktop, IntPtr root)
    {
        _x = x;
        _y = y;
        _desktop = desktop;
        _root = root;
    }

    /// <summary>Descobre a pasta embaixo do ponto (chamar na thread da interface).</summary>
    public static DropTarget Capture(int x, int y)
    {
        try
        {
            var hwnd = WindowFromPoint(new POINT { X = x, Y = y });
            var root = hwnd == IntPtr.Zero ? IntPtr.Zero : GetAncestor(hwnd, 2);
            var cls = new System.Text.StringBuilder(64);
            if (root != IntPtr.Zero) GetClassName(root, cls, cls.Capacity);
            var name = cls.ToString();
            if (name is "Progman" or "WorkerW")
                return new DropTarget(x, y, true, root) { Folder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) };
            if (name is "CabinetWClass" or "ExploreWClass")
            {
                var window = FindExplorer(root);
                string? path = null;
                try { path = (string?)window?.Document.Folder.Self.Path; } catch (Exception) { }
                if (path is not null && Directory.Exists(path))
                    return new DropTarget(x, y, false, root) { Folder = path };
            }
        }
        catch (Exception)
        {
            // Sem pasta conhecida: salva ao lado do original.
        }
        return new DropTarget(x, y, false, IntPtr.Zero);
    }

    /// <summary>Pasta de saída para um arquivo de origem.</summary>
    public string FolderFor(string source) =>
        Folder ?? Path.GetDirectoryName(source.TrimEnd('\\')) ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    /// <summary>
    /// Coloca os arquivos criados no ponto onde foram soltos e deixa selecionados.
    /// Melhor esforço: o Explorer leva um instante para "ver" o arquivo novo, então tenta algumas vezes.
    /// </summary>
    public void Place(IReadOnlyList<string> created, Dispatcher ui)
    {
        if (created.Count == 0 || Folder is null) return;
        var attempts = 0;
        var timer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(350) };
        timer.Tick += (_, _) =>
        {
            attempts++;
            var ok = false;
            try { ok = TryPlace(created); } catch (Exception) { }
            if (ok && attempts >= 2 || attempts >= 5) timer.Stop();
        };
        timer.Start();
    }

    private bool TryPlace(IReadOnlyList<string> created)
    {
        var view = GetView();
        if (view is null) return false;
        try
        {
            if (view is not IOleWindow ole || ole.GetWindow(out var viewHwnd) != 0) return false;
            var placed = false;
            for (var i = 0; i < created.Count; i++)
            {
                if (SHParseDisplayName(created[i], IntPtr.Zero, out var full, 0, out _) != 0 || full == IntPtr.Zero) continue;
                try
                {
                    var child = ILFindLastID(full);
                    var pt = new POINT { X = _x + i * 24, Y = _y + i * 24 };
                    ScreenToClient(viewHwnd, ref pt);
                    // Centraliza o ícone no ponto (ícones padrão têm ~75 px de largura na área de trabalho).
                    pt.X -= 36;
                    pt.Y -= 30;
                    var flags = SvsiPositionItem | SvsiSelect | (i == 0 ? SvsiDeselectOthers : 0) | SvsiEnsureVisible;
                    var pidls = new[] { child };
                    var points = new[] { pt };
                    if (((IFolderView)view).SelectAndPositionItems(1, pidls, points, flags) == 0) placed = true;
                }
                finally
                {
                    Marshal.FreeCoTaskMem(full);
                }
            }
            return placed;
        }
        finally
        {
            Marshal.ReleaseComObject(view);
        }
    }

    /// <summary>A "vista" de arquivos (IShellView) da área de trabalho ou da janela do Explorer.</summary>
    private object? GetView()
    {
        object? browserHost = null;
        if (_desktop)
        {
            dynamic windows = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"))!)!;
            object loc = 0, root = 0;
            browserHost = windows.FindWindowSW(ref loc, ref root, SwcDesktop, out int _, SwfoNeedDispatch);
        }
        else if (_root != IntPtr.Zero)
        {
            browserHost = FindExplorer(_root);
        }
        if (browserHost is not IServiceProvider sp) return null;
        var sid = SidTopLevelBrowser;
        var iid = typeof(IShellBrowser).GUID;
        if (sp.QueryService(ref sid, ref iid, out var browserPtr) != 0 || browserPtr == IntPtr.Zero) return null;
        var browser = (IShellBrowser)Marshal.GetObjectForIUnknown(browserPtr);
        Marshal.Release(browserPtr);
        return browser.QueryActiveShellView(out var view) == 0 ? view : null;
    }

    /// <summary>A janela do Explorer com esse identificador (no Windows 11, a aba que está visível).</summary>
    private static dynamic? FindExplorer(IntPtr root)
    {
        var type = Type.GetTypeFromProgID("Shell.Application");
        if (type is null) return null;
        dynamic shell = Activator.CreateInstance(type)!;
        var matches = new List<dynamic>();
        foreach (dynamic w in shell.Windows())
        {
            try
            {
                if (new IntPtr((long)w.HWND) == root) matches.Add(w);
            }
            catch (Exception)
            {
                // Janela fechando.
            }
        }
        if (matches.Count <= 1) return matches.Count == 1 ? matches[0] : null;

        // Várias abas: a aba ativa é a primeira "ShellTabWindowClass" visível.
        var activeTab = FindWindowEx(root, IntPtr.Zero, "ShellTabWindowClass", null);
        foreach (var w in matches)
        {
            try
            {
                if (w is IServiceProvider sp)
                {
                    var sid = SidTopLevelBrowser;
                    var iid = typeof(IShellBrowser).GUID;
                    if (sp.QueryService(ref sid, ref iid, out var ptr) == 0 && ptr != IntPtr.Zero)
                    {
                        var browser = (IShellBrowser)Marshal.GetObjectForIUnknown(ptr);
                        Marshal.Release(ptr);
                        if (browser.GetWindow(out var tab) == 0 && tab == activeTab) return w;
                    }
                }
            }
            catch (Exception)
            {
                // Tenta a próxima.
            }
        }
        return matches[0];
    }

    // ---------- Interop do shell ----------

    private const int SwcDesktop = 8;
    private const int SwfoNeedDispatch = 1;
    private const uint SvsiSelect = 0x1, SvsiDeselectOthers = 0x4, SvsiEnsureVisible = 0x8, SvsiPositionItem = 0x80;
    private static readonly Guid SidTopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        [PreserveSig] int QueryService(ref Guid guidService, ref Guid riid, out IntPtr ppvObject);
    }

    [ComImport, Guid("00000114-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IOleWindow
    {
        [PreserveSig] int GetWindow(out IntPtr phwnd);
        [PreserveSig] int ContextSensitiveHelp(bool fEnterMode);
    }

    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        [PreserveSig] int GetWindow(out IntPtr phwnd);
        [PreserveSig] int ContextSensitiveHelp(bool fEnterMode);
        [PreserveSig] int InsertMenusSB(IntPtr hmenuShared, IntPtr lpMenuWidths);
        [PreserveSig] int SetMenuSB(IntPtr hmenuShared, IntPtr holemenuRes, IntPtr hwndActiveObject);
        [PreserveSig] int RemoveMenusSB(IntPtr hmenuShared);
        [PreserveSig] int SetStatusTextSB(IntPtr pszStatusText);
        [PreserveSig] int EnableModelessSB(bool fEnable);
        [PreserveSig] int TranslateAcceleratorSB(IntPtr pmsg, ushort wID);
        [PreserveSig] int BrowseObject(IntPtr pidl, uint wFlags);
        [PreserveSig] int GetViewStateStream(uint grfMode, out IntPtr ppStrm);
        [PreserveSig] int GetControlWindow(uint id, out IntPtr phwnd);
        [PreserveSig] int SendControlMsg(uint id, uint uMsg, IntPtr wParam, IntPtr lParam, out IntPtr pret);
        [PreserveSig] int QueryActiveShellView([MarshalAs(UnmanagedType.IUnknown)] out object ppshv);
        [PreserveSig] int OnViewWindowActive(IntPtr pshv);
        [PreserveSig] int SetToolbarItems(IntPtr lpButtons, uint nButtons, uint uFlags);
    }

    [ComImport, Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFolderView
    {
        [PreserveSig] int GetCurrentViewMode(out uint pViewMode);
        [PreserveSig] int SetCurrentViewMode(uint viewMode);
        [PreserveSig] int GetFolder(ref Guid riid, out IntPtr ppv);
        [PreserveSig] int Item(int iItemIndex, out IntPtr ppidl);
        [PreserveSig] int ItemCount(uint uFlags, out int pcItems);
        [PreserveSig] int Items(uint uFlags, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetSelectionMarkedItem(out int piItem);
        [PreserveSig] int GetFocusedItem(out int piItem);
        [PreserveSig] int GetItemPosition(IntPtr pidl, out POINT ppt);
        [PreserveSig] int GetSpacing(ref POINT ppt);
        [PreserveSig] int GetDefaultSpacing(out POINT ppt);
        [PreserveSig] int GetAutoArrange();
        [PreserveSig] int SelectItem(int iItem, uint dwFlags);
        [PreserveSig] int SelectAndPositionItems(uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, [MarshalAs(UnmanagedType.LPArray)] POINT[] apt, uint dwFlags);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string pszName, IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);
    [DllImport("shell32.dll")] private static extern IntPtr ILFindLastID(IntPtr pidl);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? title);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);
}
