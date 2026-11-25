using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;

namespace Assistente.App.Services;

/// <summary>
/// Mantém a barra de tarefas do Windows escondida de vez (em todos os monitores).
/// O Explorer tenta mostrar a barra de novo quando o mouse encosta embaixo da tela
/// (o "ocultar automaticamente"), ao abrir o Iniciar, ao trocar de resolução etc.
/// Aqui a gente escuta o próprio Windows avisando "a barra apareceu" e esconde no mesmo
/// instante, antes de ela ser desenhada; um relógio rápido cobre qualquer caso que escape.
/// </summary>
public static class TaskbarBlocker
{
    private const uint EventObjectShow = 0x8002;
    private const uint EventObjectLocationChange = 0x800B;
    private const uint WinEventOutOfContext = 0x0000;
    private const uint WinEventSkipOwnProcess = 0x0002;
    private const int ObjIdWindow = 0;

    private static IntPtr _hook;
    private static uint _hookedPid;
    private static WinEventDelegate? _callback; // guardado para o GC não recolher
    private static DispatcherTimer? _timer;

    public static bool Active { get; private set; }

    /// <summary>Começa a bloquear: esconde a barra agora e não deixa mais ela voltar.</summary>
    public static void Start()
    {
        if (Active) return;
        Active = true;
        _callback ??= OnWinEvent;
        Hook();
        HideNow();
        _timer ??= new DispatcherTimer(DispatcherPriority.Send) { Interval = TimeSpan.FromMilliseconds(150) };
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        _timer.Start();
    }

    /// <summary>Para de bloquear (quem chama decide se mostra a barra de novo).</summary>
    public static void Stop()
    {
        if (!Active) return;
        Active = false;
        _timer?.Stop();
        Unhook();
    }

    private static void OnTick(object? sender, EventArgs e)
    {
        // O Explorer pode ter reiniciado (processo novo): escuta o novo.
        if (ExplorerPid() != _hookedPid) Hook();
        HideNow();
    }

    private static void HideNow()
    {
        try
        {
            if (Docking.Native.AnyTaskbarVisible()) Docking.Native.SetTaskbarVisible(false);
        }
        catch (Exception)
        {
            // Sem barra de tarefas (ex.: outro shell).
        }
    }

    private static void Hook()
    {
        Unhook();
        var pid = ExplorerPid();
        if (pid == 0) return;
        // Só eventos do Explorer: "janela apareceu" e "janela mexeu" (a barra subindo ao encostar o mouse).
        _hook = SetWinEventHook(EventObjectShow, EventObjectLocationChange, IntPtr.Zero, _callback!, pid, 0,
            WinEventOutOfContext | WinEventSkipOwnProcess);
        _hookedPid = _hook != IntPtr.Zero ? pid : 0;
    }

    private static void Unhook()
    {
        if (_hook != IntPtr.Zero) UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
        _hookedPid = 0;
    }

    private static uint ExplorerPid()
    {
        var tray = FindWindow("Shell_TrayWnd", null);
        if (tray == IntPtr.Zero) return 0;
        GetWindowThreadProcessId(tray, out var pid);
        return pid;
    }

    private static readonly StringBuilder ClassBuffer = new(64);

    private static void OnWinEvent(IntPtr hook, uint type, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (!Active || hwnd == IntPtr.Zero || idObject != ObjIdWindow) return;
        if (type != EventObjectShow && type != EventObjectLocationChange) return;
        ClassBuffer.Clear();
        GetClassName(hwnd, ClassBuffer, ClassBuffer.Capacity);
        var name = ClassBuffer.ToString();
        if (name is not ("Shell_TrayWnd" or "Shell_SecondaryTrayWnd")) return;
        if (IsWindowVisible(hwnd)) ShowWindow(hwnd, 0 /* SW_HIDE */);
    }

    private delegate void WinEventDelegate(IntPtr hook, uint type, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventDelegate callback, uint pid, uint thread, uint flags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? cls, string? title);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int cmd);
}
