using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Assistente.App.Services;

public sealed class ClipboardEntry
{
    public ClipboardEntry(string text)
    {
        Text = text;
        CopiedAt = DateTime.Now;
    }

    public string Text { get; }
    public DateTime CopiedAt { get; set; }

    /// <summary>Texto resumido para a lista (uma linha, até 200 caracteres).</summary>
    public string Preview
    {
        get
        {
            var oneLine = string.Join(" ", Text.Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
            return oneLine.Length > 200 ? oneLine[..200] + "…" : oneLine;
        }
    }

    public string Details => $"{CopiedAt:HH:mm:ss} · {Text.Length} caracteres";
}

/// <summary>
/// Guarda tudo que é copiado (Ctrl+C) enquanto o app está aberto.
/// Usa AddClipboardFormatListener, o jeito oficial do Windows de ser avisado de cópias.
/// O histórico fica só na memória: nada é gravado em disco.
/// </summary>
public sealed class ClipboardHistory
{
    public const int MaxEntries = 200;

    private const int WmClipboardUpdate = 0x031D;

    // Formatos que gerenciadores de senha usam para pedir que a cópia não seja registrada.
    private static readonly string[] ExcludeFormats =
    {
        "ExcludeClipboardContentFromMonitorProcessing",
        "Clipboard Viewer Ignore",
    };

    public static ClipboardHistory Instance { get; } = new();

    private Dispatcher? _dispatcher;

    public ObservableCollection<ClipboardEntry> Entries { get; } = new();

    /// <summary>Quando true, novas cópias não são registradas.</summary>
    public bool Paused { get; set; }

    public void Attach(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var source = HwndSource.FromHwnd(hwnd);
        if (source is null) return;

        _dispatcher = window.Dispatcher;
        source.AddHook(WndProc);
        AddClipboardFormatListener(hwnd);
        window.Closed += (_, _) => RemoveClipboardFormatListener(hwnd);

        // Já guarda o que estava copiado quando o app abriu.
        Capture();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmClipboardUpdate && !Paused)
            _dispatcher?.BeginInvoke(DispatcherPriority.Background, new Action(Capture));
        return IntPtr.Zero;
    }

    private void Capture()
    {
        var text = ReadText();
        if (string.IsNullOrWhiteSpace(text)) return;

        var existing = Entries.FirstOrDefault(e => e.Text == text);
        if (existing is not null)
        {
            // Já existe: só traz para o topo.
            existing.CopiedAt = DateTime.Now;
            var index = Entries.IndexOf(existing);
            if (index > 0) Entries.Move(index, 0);
            return;
        }

        Entries.Insert(0, new ClipboardEntry(text));
        while (Entries.Count > MaxEntries) Entries.RemoveAt(Entries.Count - 1);
    }

    private static string? ReadText()
    {
        // Outro programa pode estar com a área de transferência aberta: tenta algumas vezes.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (ExcludeFormats.Any(Clipboard.ContainsData)) return null;
                return Clipboard.ContainsText() ? Clipboard.GetText() : null;
            }
            catch (COMException)
            {
                Thread.Sleep(40);
            }
            catch (ExternalException)
            {
                Thread.Sleep(40);
            }
        }
        return null;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
}
