using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Assistente.App.Services;

namespace Assistente.App;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private TrayIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Só uma instância por usuário.
        var mutex = new Mutex(true, @"Local\Assistente.Windows.SingleInstance", out var createdNew);
        if (!createdNew && !(UpdateService.WasJustUpdated(e.Args) && WaitForPreviousInstance(mutex)))
        {
            mutex.Dispose();
            Shutdown();
            return;
        }
        _singleInstance = mutex;

        DispatcherUnhandledException += OnUnhandledException;
        // Se o app fechar de repente, devolve a barra de tarefas do Windows.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => RestoreTaskbarSafe();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => RestoreTaskbarSafe();
        base.OnStartup(e);

        var settings = AppSettings.Load();
        var window = new MainWindow(settings);
        MainWindow = window;
        window.Show();

        _tray = new TrayIcon(window, settings);
        MetricsService.Instance.Start();
        Widgets.WidgetManager.Start(window);
        Docking.DockWindow.Apply(settings); // também devolve a barra do Windows se a v0.9.0 escondeu
        MenuBar.MenuBarWindow.Apply(settings);

        if (settings.ApplyMacTaskbarOnStart)
        {
            settings.ApplyMacTaskbarOnStart = false;
            TaskbarStyle.ApplyMacLook(settings);
            settings.Save();
        }
        UpdateService.Instance.Start(e.Args);
    }

    private static void RestoreTaskbarSafe()
    {
        MenuBar.MenuBarWindow.ReleaseSafe();
        try
        {
            if (Assistente.App.MainWindow.Current is { } main && main.Settings.DockTaskbarWasAutoHide is not null)
                Docking.DockWindow.RestoreTaskbar(main.Settings);
        }
        catch (Exception)
        {
            // Melhor esforço.
        }
    }

    /// <summary>Depois de uma atualização, espera a versão antiga terminar de fechar.</summary>
    private static bool WaitForPreviousInstance(Mutex mutex)
    {
        try
        {
            return mutex.WaitOne(TimeSpan.FromSeconds(20));
        }
        catch (AbandonedMutexException)
        {
            return true; // A versão antiga fechou sem liberar: agora o mutex é nosso.
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (Assistente.App.MainWindow.Current is { } main) Docking.DockWindow.Shutdown(main.Settings);
        MenuBar.MenuBarWindow.ReleaseSafe();
        _tray?.Dispose();
        if (_singleInstance is not null)
        {
            _singleInstance.ReleaseMutex();
            _singleInstance.Dispose();
        }
        base.OnExit(e);
    }

    private static DateTime _lastErrorDialog = DateTime.MinValue;

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        // No máximo um aviso por minuto (um erro repetido não enche a tela de janelas).
        if (DateTime.Now - _lastErrorDialog < TimeSpan.FromMinutes(1)) return;
        _lastErrorDialog = DateTime.Now;
        MessageBox.Show($"Ocorreu um erro inesperado:\n\n{e.Exception.Message}", "Assistente",
            MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
