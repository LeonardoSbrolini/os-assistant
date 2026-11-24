using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Assistente.App.Services;

/// <summary>
/// Atualização automática pelas "releases" do GitHub.
///
/// Fluxo: ao abrir (e a cada 6 horas) consulta a última release; se a versão for maior,
/// baixa o .exe novo ao lado do atual, renomeia o atual para .old (o Windows permite
/// renomear um .exe em uso), coloca o novo no lugar e reabre. O .old é apagado na próxima abertura.
///
/// Assets esperados na release: "Assistente.exe" (completo) e "Assistente-leve.exe" (precisa do .NET).
/// </summary>
public sealed class UpdateService
{
    private const string UpdatedArg = "--atualizado";
    private const string FullAssetName = "Assistente.exe";
    private const string LightAssetName = "Assistente-leve.exe";

    public static UpdateService Instance { get; } = new();

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromHours(6) };
    private readonly DispatcherTimer _waitForCollapse = new() { Interval = TimeSpan.FromSeconds(30) };
    private bool _busy;
    private string? _pendingNewPath;
    private Version? _pendingVersion;

    public static Version CurrentVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);

    public static string CurrentVersionText => $"{CurrentVersion.Major}.{CurrentVersion.Minor}.{CurrentVersion.Build}";

    private static string? Metadata(string key) =>
        Assembly.GetEntryAssembly()?.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == key)?.Value;

    private static string? Repository => Metadata("UpdateRepository") is { Length: > 0 } r ? r : null;

    private static bool IsLightBuild => string.Equals(Metadata("SelfContained"), "false", StringComparison.OrdinalIgnoreCase);

    private static bool AutoUpdateEnabled => MainWindow.Current?.Settings.AutoUpdate ?? true;

    /// <summary>Data/hora da última verificação concluída.</summary>
    public DateTime? LastCheck { get; private set; }

    public static bool WasJustUpdated(string[] args) => args.Contains(UpdatedArg);

    public void Start(string[] args)
    {
        CleanupOldFile();

        if (WasJustUpdated(args))
            TrayIcon.Notify("Assistente atualizado", $"Agora você está na versão {CurrentVersionText}.");

        _timer.Tick += async (_, _) =>
        {
            if (AutoUpdateEnabled) await CheckAsync(manual: false);
        };
        _timer.Start();
        _waitForCollapse.Tick += (_, _) => TryApplyPending();

        // Primeira verificação alguns segundos depois de abrir, para não atrasar a inicialização.
        var first = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        first.Tick += async (_, _) =>
        {
            first.Stop();
            if (AutoUpdateEnabled) await CheckAsync(manual: false);
        };
        first.Start();
    }

    /// <summary>Verifica e instala uma versão nova, se houver.</summary>
    /// <param name="manual">Se true (menu "Verificar atualizações"), sempre mostra o resultado.</param>
    /// <param name="showProgress">Mostra a janelinha com a barra de download, velocidade e tempo restante.</param>
    /// <returns>Mensagem com o resultado (mostrada na tela de configurações).</returns>
    public async Task<string> CheckAsync(bool manual, bool showProgress = false)
    {
        if (_busy)
        {
            _window?.Activate();
            return "Já estou verificando…";
        }
        _busy = true;
        UpdateProgressWindow? window = null;
        if (showProgress)
        {
            window = _window = new UpdateProgressWindow();
            window.Closed += (_, _) => { if (_window == window) _window = null; };
            window.Show();
            window.Activate();
        }
        // Com a janelinha aberta, o resultado aparece nela (sem balão do Windows).
        string Done(string message, bool ok)
        {
            LastCheck = DateTime.Now;
            if (window is not null)
            {
                if (ok) window.UpToDate(message);
                else window.Failed(message);
            }
            else if (manual)
            {
                TrayIcon.Notify("Atualizações", message);
            }
            return message;
        }

        // Não é descartado: a janela pode ser fechada depois que a verificação acabou.
        var cancel = new CancellationTokenSource();
        if (window is not null)
        {
            window.CancelRequested += () => cancel.Cancel();
            window.Closed += (_, _) => cancel.Cancel();
        }

        var newPath = Environment.ProcessPath is { } p ? p + ".new" : null;
        try
        {
            if (Repository is not { } repo || Environment.ProcessPath is not { } exePath)
            {
                return Done("Atualização automática não configurada nesta versão.", ok: false);
            }

            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Assistente-Atualizador", CurrentVersionText));
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var response = await http.GetAsync($"https://api.github.com/repos/{repo}/releases/latest", cancel.Token);
            if (!response.IsSuccessStatusCode)
            {
                return Done("Ainda não há versões publicadas.", ok: false);
            }

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel.Token));
            var root = json.RootElement;
            var tag = root.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest) || Normalize(latest) <= Normalize(CurrentVersion))
            {
                return Done($"Você já está na versão mais recente ({CurrentVersionText}).", ok: true);
            }

            var assetName = IsLightBuild ? LightAssetName : FullAssetName;
            var asset = root.GetProperty("assets").EnumerateArray()
                .FirstOrDefault(a => a.GetProperty("name").GetString() == assetName);
            var url = asset.ValueKind == JsonValueKind.Object ? asset.GetProperty("browser_download_url").GetString() : null;
            if (url is null)
            {
                return Done($"A versão {tag} não tem o arquivo {assetName}.", ok: false);
            }
            var expected = asset.TryGetProperty("size", out var sizeProp) && sizeProp.TryGetInt64(out var sz) ? sz : 0;

            var versionText = tag.TrimStart('v', 'V');
            if (window is null) TrayIcon.Notify("Atualizando o Assistente", $"Baixando a versão {tag}…");
            else window.Downloading(versionText, expected);

            using (var download = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel.Token))
            {
                download.EnsureSuccessStatusCode();
                var total = download.Content.Headers.ContentLength ?? expected;
                await using var source = await download.Content.ReadAsStreamAsync(cancel.Token);
                await using var file = File.Create(newPath!);
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancel.Token)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), cancel.Token);
                    done += read;
                    window?.Progress(done, total);
                }
                if (total > 0 && done < total) throw new IOException("o download veio incompleto. Tente de novo.");
            }

            _pendingNewPath = newPath;
            _pendingVersion = latest;
            if (window is not null)
            {
                // Quem pediu a atualização vê terminar: instala logo depois de mostrar o "pronto".
                window.Installing(versionText);
                var restart = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.6) };
                restart.Tick += (_, _) =>
                {
                    restart.Stop();
                    TryApplyPending(force: true);
                };
                restart.Start();
            }
            else
            {
                TryApplyPending();
            }
            return $"Versão {tag} baixada. O app vai reiniciar agora.";
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            DeleteQuietly(newPath);
            LastCheck = DateTime.Now;
            window?.Close();
            return "Atualização cancelada.";
        }
        catch (Exception ex)
        {
            DeleteQuietly(newPath);
            return Done($"Não foi possível atualizar: {ex.Message}", ok: false);
        }
        finally
        {
            _busy = false;
        }
    }

    private UpdateProgressWindow? _window;

    private static void DeleteQuietly(string? path)
    {
        try
        {
            if (path is not null && File.Exists(path)) File.Delete(path);
        }
        catch (Exception)
        {
            // Apagado na próxima abertura.
        }
    }

    /// <summary>Troca o .exe e reinicia, mas só com o painel recolhido para não atrapalhar.</summary>
    private void TryApplyPending(bool force = false)
    {
        if (_pendingNewPath is null || Environment.ProcessPath is not { } exePath) return;

        if (!force && MainWindow.Current is { IsExpanded: true })
        {
            _waitForCollapse.Start();
            return;
        }
        _waitForCollapse.Stop();

        var oldPath = exePath + ".old";
        try
        {
            if (File.Exists(oldPath)) File.Delete(oldPath);
            File.Move(exePath, oldPath);
            File.Move(_pendingNewPath, exePath);
        }
        catch (Exception ex)
        {
            // Desfaz se algo deu errado no meio do caminho.
            if (!File.Exists(exePath) && File.Exists(oldPath)) File.Move(oldPath, exePath);
            _pendingNewPath = null;
            var message = $"Não foi possível instalar a versão {_pendingVersion}: {ex.Message}";
            if (_window is not null) _window.Failed(message);
            else TrayIcon.Notify("Atualizações", message);
            return;
        }

        _pendingNewPath = null;
        Process.Start(new ProcessStartInfo(exePath, UpdatedArg) { UseShellExecute = false });
        Application.Current.Shutdown();
    }

    private static void CleanupOldFile()
    {
        if (Environment.ProcessPath is not { } exePath) return;
        foreach (var leftover in new[] { exePath + ".old", exePath + ".new" })
        {
            try
            {
                if (File.Exists(leftover)) File.Delete(leftover);
            }
            catch (Exception)
            {
                // A versão antiga pode ainda estar fechando; tenta de novo na próxima abertura.
            }
        }
    }

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));
}
