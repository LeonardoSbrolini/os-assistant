using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Assistente.App.Services;

/// <summary>
/// Papel de parede escolhido no Assistente, uma imagem para todas as telas ou uma para cada.
/// Em computadores da empresa (AD), a política de grupo costuma colocar de volta o papel de
/// parede padrão: com "Manter" ligado, o Assistente percebe a troca e aplica o seu de novo
/// (ao abrir, ao desbloquear, quando o Windows avisa que o fundo mudou e a cada 30 s).
/// </summary>
public static class WallpaperService
{
    public static readonly (string Key, string Name, DesktopWallpaperPosition Position)[] Fits =
    {
        ("preencher", "Preencher", DesktopWallpaperPosition.Fill),
        ("ajustar", "Ajustar", DesktopWallpaperPosition.Fit),
        ("esticar", "Esticar", DesktopWallpaperPosition.Stretch),
        ("centralizar", "Centralizar", DesktopWallpaperPosition.Center),
        ("lado-a-lado", "Lado a lado", DesktopWallpaperPosition.Tile),
        ("estender", "Estender nas telas", DesktopWallpaperPosition.Span),
    };

    public sealed record Screen(string Id, int Index, int X, int Width, int Height, bool Primary, string? Current);

    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Assistente", "PapelDeParede");

    private static AppSettings? _settings;
    private static DispatcherTimer? _timer;
    private static DispatcherTimer? _debounce;
    private static bool _applying;
    private static DateTime _lastRestore = DateTime.MinValue;

    /// <summary>Quantas vezes o Assistente já colocou o papel de parede de volta desde que abriu.</summary>
    public static int RestoredCount { get; private set; }

    /// <summary>Quando foi a última vez que precisou colocar de volta.</summary>
    public static DateTime? LastRestored { get; private set; }

    /// <summary>Começa a vigiar o papel de parede (chamado ao abrir o app).</summary>
    public static void Start(AppSettings settings)
    {
        _settings = settings;
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); Enforce(); };
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _timer.Tick += (_, _) => Enforce();
        _timer.Start();

        // O Windows avisa quando o fundo muda (política de grupo, outro programa...) e ao desbloquear.
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category is UserPreferenceCategory.Desktop or UserPreferenceCategory.General) Later();
        };
        SystemEvents.SessionSwitch += (_, e) =>
        {
            if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon or SessionSwitchReason.ConsoleConnect
                or SessionSwitchReason.RemoteConnect) Later();
        };
        SystemEvents.DisplaySettingsChanged += (_, _) => Later();

        Later();
    }

    private static void Later()
    {
        if (_debounce is null) return;
        _debounce.Dispatcher.BeginInvoke(new Action(() =>
        {
            _debounce.Stop();
            _debounce.Start();
        }));
    }

    /// <summary>Tem alguma imagem escolhida no Assistente?</summary>
    public static bool HasChoice(AppSettings s) =>
        !string.IsNullOrEmpty(s.WallpaperAll) || s.WallpaperMonitors is { Count: > 0 };

    /// <summary>Se "Manter" estiver ligado e alguém trocou o papel de parede, coloca o escolhido de volta.</summary>
    public static void Enforce()
    {
        if (_settings is not { WallpaperKeep: true } s || !HasChoice(s) || _applying) return;
        // Se o Windows desfaz na mesma hora, não fica brigando sem parar: no máximo a cada 15 s.
        if (DateTime.Now - _lastRestore < TimeSpan.FromSeconds(15)) return;
        try
        {
            if (IsApplied(s)) return;
            _lastRestore = DateTime.Now;
            Apply(s);
            RestoredCount++;
            LastRestored = DateTime.Now;
        }
        catch (Exception)
        {
            // Tenta de novo na próxima volta.
        }
    }

    /// <summary>Imagem que cada tela deveria ter (null = deixar como está).</summary>
    private static string? Wanted(AppSettings s, string monitorId)
    {
        if (s.WallpaperMonitors is { } map && map.TryGetValue(monitorId, out var own) && File.Exists(own)) return own;
        return !string.IsNullOrEmpty(s.WallpaperAll) && File.Exists(s.WallpaperAll) ? s.WallpaperAll : null;
    }

    private static bool Same(string? a, string? b) =>
        !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) &&
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>O papel de parede que está na tela é o escolhido?</summary>
    public static bool IsApplied(AppSettings s)
    {
        var position = FitPosition(s.WallpaperFit);
        try
        {
            var dw = Create();
            try
            {
                var monitors = Monitors(dw);
                if (monitors.Count == 0) return true;
                foreach (var m in monitors)
                {
                    var want = Wanted(s, m.Id);
                    if (want is not null && !Same(m.Current, want)) return false;
                }
                return dw.GetPosition() == position;
            }
            finally { Marshal.ReleaseComObject(dw); }
        }
        catch (Exception)
        {
            // Sem a interface moderna: confere o registro (uma imagem para todas as telas).
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
            var want = s.WallpaperAll ?? s.WallpaperMonitors?.Values.FirstOrDefault();
            return want is null || Same(key?.GetValue("WallpaperPath") as string ?? key?.GetValue("Wallpaper") as string, want);
        }
    }

    /// <summary>Coloca o papel de parede escolhido em cada tela.</summary>
    public static void Apply(AppSettings s)
    {
        if (!HasChoice(s)) return;
        _applying = true;
        try
        {
            var position = FitPosition(s.WallpaperFit);
            var single = s.WallpaperMonitors is not { Count: > 0 } ? s.WallpaperAll : null;

            // Se a empresa definiu o papel de parede por política, o Windows sempre volta para o
            // da política. Quando dá, a política (do próprio usuário) passa a apontar para o escolhido.
            var policyImage = single ?? s.WallpaperAll ?? s.WallpaperMonitors!.Values.First();
            TryOverridePolicy(policyImage, position);

            var done = false;
            try
            {
                var dw = Create();
                try
                {
                    dw.SetPosition(position);
                    if (single is not null && File.Exists(single))
                        dw.SetWallpaper(null, single);
                    else
                        foreach (var m in Monitors(dw))
                            if (Wanted(s, m.Id) is { } img) dw.SetWallpaper(m.Id, img);
                    done = true;
                }
                finally { Marshal.ReleaseComObject(dw); }
            }
            catch (Exception)
            {
                // Cai para o jeito antigo logo abaixo.
            }

            if (!done && policyImage is not null && File.Exists(policyImage))
            {
                SetRegistryStyle(position);
                if (!SystemParametersInfo(SpiSetDeskWallpaper, 0, policyImage, SpifUpdateIniFile | SpifSendChange))
                    throw new InvalidOperationException("O Windows não aceitou trocar o papel de parede.");
            }
        }
        finally
        {
            _applying = false;
        }
    }

    /// <summary>Telas ligadas agora, com o papel de parede de cada uma.</summary>
    public static List<Screen> CurrentMonitors()
    {
        try
        {
            var dw = Create();
            try { return Monitors(dw); }
            finally { Marshal.ReleaseComObject(dw); }
        }
        catch (Exception)
        {
            return new List<Screen>();
        }
    }

    private static List<Screen> Monitors(IDesktopWallpaper dw)
    {
        var list = new List<Screen>();
        var count = dw.GetMonitorDevicePathCount();
        for (uint i = 0; i < count; i++)
        {
            try
            {
                var id = dw.GetMonitorDevicePathAt(i);
                var r = dw.GetMonitorRECT(id);
                if (r.Right - r.Left <= 0) continue; // tela desligada
                string? current = null;
                try { current = dw.GetWallpaper(id); } catch (Exception) { }
                list.Add(new Screen(id, 0, r.Left, r.Right - r.Left, r.Bottom - r.Top, r.Left == 0 && r.Top == 0, current));
            }
            catch (Exception)
            {
                // Tela que já foi ligada mas não está conectada.
            }
        }
        // Numeradas da esquerda para a direita, como as telas estão na mesa.
        return list.OrderBy(m => m.X).Select((m, i) => m with { Index = i + 1 }).ToList();
    }

    /// <summary>Copia a imagem para a pasta do Assistente (continua valendo se o original sumir).</summary>
    public static string Import(string source)
    {
        Directory.CreateDirectory(Folder);
        if (Path.GetDirectoryName(Path.GetFullPath(source))!.Equals(Folder, StringComparison.OrdinalIgnoreCase)) return source;
        var name = Path.GetFileNameWithoutExtension(source);
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        if (name.Length > 40) name = name[..40];
        var ext = Path.GetExtension(source);
        if (string.IsNullOrEmpty(ext)) ext = ".jpg";
        var dest = Path.Combine(Folder, $"{DateTime.Now:yyyyMMdd-HHmmss}-{name}{ext}");
        File.Copy(source, dest, overwrite: true);
        return dest;
    }

    /// <summary>Apaga da pasta do Assistente as imagens que ninguém usa mais.</summary>
    public static void Cleanup(AppSettings s)
    {
        try
        {
            if (!Directory.Exists(Folder)) return;
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (s.WallpaperAll is { } all) used.Add(Path.GetFullPath(all));
            if (s.WallpaperMonitors is { } map) foreach (var p in map.Values) used.Add(Path.GetFullPath(p));
            foreach (var file in Directory.GetFiles(Folder))
                if (!used.Contains(Path.GetFullPath(file))) File.Delete(file);
        }
        catch (Exception)
        {
            // Melhor esforço.
        }
    }

    public static DesktopWallpaperPosition FitPosition(string? key)
    {
        foreach (var f in Fits)
            if (f.Key == key) return f.Position;
        return DesktopWallpaperPosition.Fill;
    }

    // ---------- Política de grupo e registro ----------

    private const string PolicyKey = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";

    /// <summary>Há um papel de parede imposto por política para este usuário?</summary>
    public static bool PolicyActive()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PolicyKey);
            return key?.GetValue("Wallpaper") is string { Length: > 0 };
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void TryOverridePolicy(string? image, DesktopWallpaperPosition position)
    {
        if (image is null) return;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PolicyKey, writable: true);
            if (key?.GetValue("Wallpaper") is not string current || current.Length == 0) return;
            if (!Same(current, image)) key.SetValue("Wallpaper", image, RegistryValueKind.String);
            var (style, _) = StyleValues(position);
            key.SetValue("WallpaperStyle", style, RegistryValueKind.String);
        }
        catch (Exception)
        {
            // Sem permissão: a empresa travou de vez; o Assistente reaplica sempre que voltar.
        }
    }

    private static (string Style, string Tile) StyleValues(DesktopWallpaperPosition p) => p switch
    {
        DesktopWallpaperPosition.Center => ("0", "0"),
        DesktopWallpaperPosition.Tile => ("0", "1"),
        DesktopWallpaperPosition.Stretch => ("2", "0"),
        DesktopWallpaperPosition.Fit => ("6", "0"),
        DesktopWallpaperPosition.Span => ("22", "0"),
        _ => ("10", "0"),
    };

    private static void SetRegistryStyle(DesktopWallpaperPosition position)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop", writable: true);
            var (style, tile) = StyleValues(position);
            key.SetValue("WallpaperStyle", style);
            key.SetValue("TileWallpaper", tile);
        }
        catch (Exception)
        {
            // Melhor esforço.
        }
    }

    // ---------- Interop ----------

    private const uint SpiSetDeskWallpaper = 0x0014;
    private const uint SpifUpdateIniFile = 0x01;
    private const uint SpifSendChange = 0x02;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, string vparam, uint winIni);

    private static IDesktopWallpaper Create() =>
        (IDesktopWallpaper)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD"))!)!;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetMonitorDevicePathAt(uint monitorIndex);
        uint GetMonitorDevicePathCount();
        NativeRect GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorId);
        void SetBackgroundColor(uint color);
        uint GetBackgroundColor();
        void SetPosition(DesktopWallpaperPosition position);
        DesktopWallpaperPosition GetPosition();
        void SetSlideshow(IntPtr items);
        IntPtr GetSlideshow();
        void SetSlideshowOptions(uint options, uint slideshowTick);
        void GetSlideshowOptions(out uint options, out uint slideshowTick);
        void AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, uint direction);
        uint GetStatus();
        void Enable([MarshalAs(UnmanagedType.Bool)] bool enable);
    }
}

public enum DesktopWallpaperPosition
{
    Center = 0,
    Tile = 1,
    Stretch = 2,
    Fit = 3,
    Fill = 4,
    Span = 5,
}
