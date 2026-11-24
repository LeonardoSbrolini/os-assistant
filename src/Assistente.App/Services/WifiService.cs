using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Assistente.App.Services;

/// <summary>
/// Nome da rede Wi‑Fi conectada (SSID) e o sinal, lidos do "netsh wlan show interfaces" em
/// segundo plano a cada poucos segundos (sem travar a tela). Vale para português e inglês.
/// </summary>
public static class WifiService
{
    private static readonly Regex SsidLine = new(@"^\s*SSID\s*:\s*(.*)$", RegexOptions.Multiline);
    private static readonly Regex SignalLine = new(@"^\s*(?:Sinal|Signal)\s*:\s*(\d+)\s*%", RegexOptions.Multiline | RegexOptions.IgnoreCase);
    private static DateTime _lastRead = DateTime.MinValue;
    private static bool _reading;

    /// <summary>Nome da rede Wi‑Fi conectada, ou null se não estiver no Wi‑Fi.</summary>
    public static string? Ssid
    {
        get
        {
            Poll();
            return _ssid;
        }
    }

    /// <summary>Força do sinal em % (0 se desconhecido).</summary>
    public static int Signal
    {
        get
        {
            Poll();
            return _signal;
        }
    }

    private static string? _ssid;
    private static int _signal;

    /// <summary>Lê de novo se a última leitura tiver mais de 8 segundos.</summary>
    private static void Poll()
    {
        if (_reading || DateTime.Now - _lastRead < TimeSpan.FromSeconds(8)) return;
        _reading = true;
        _lastRead = DateTime.Now;
        Task.Run(() =>
        {
            try
            {
                var text = Run();
                var m = SsidLine.Match(text);
                var ssid = m.Success ? m.Groups[1].Value.Trim() : null;
                _ssid = string.IsNullOrEmpty(ssid) ? null : ssid;
                var s = SignalLine.Match(text);
                _signal = s.Success && int.TryParse(s.Groups[1].Value, out var v) ? v : 0;
            }
            catch (Exception)
            {
                _ssid = null;
                _signal = 0;
            }
            finally
            {
                _reading = false;
            }
        });
    }

    private static bool _encodings;

    private static string Run()
    {
        if (!_encodings)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _encodings = true;
        }
        Encoding encoding;
        try
        {
            encoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        }
        catch (Exception)
        {
            encoding = Encoding.UTF8;
        }
        var psi = new ProcessStartInfo("netsh", "wlan show interfaces")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = encoding,
        };
        using var p = Process.Start(psi);
        if (p is null) return "";
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit(4000);
        return output;
    }

    /// <summary>Ícone de Wi‑Fi (Segoe Fluent/MDL2) conforme o sinal.</summary>
    public static string SignalGlyph(int signal) => signal switch
    {
        >= 75 => "",
        >= 50 => "",
        >= 25 => "",
        _ => "",
    };
}
