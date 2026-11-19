using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
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

    public static bool WasJustUpdated(string[] args) => args.Contains(UpdatedArg);

    public void Start(string[] args)
    {
        CleanupOldFile();

        if (WasJustUpdated(args))
            TrayIcon.Notify("Assistente atualizado", $"Agora você está na versão {CurrentVersionText}.");

        _timer.Tick += async (_, _) => await CheckAsync(manual: false);
        _timer.Start();
        _waitForCollapse.Tick += (_, _) => TryApplyPending();

        // Primeira verificação alguns segundos depois de abrir, para não atrasar a inicialização.
        var first = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        first.Tick += async (_, _) =>
        {
            first.Stop();
            await CheckAsync(manual: false);
        };
        first.Start();
    }

    /// <summary>Verifica e instala uma versão nova, se houver.</summary>
    /// <param name="manual">Se true (menu "Verificar atualizações"), sempre mostra o resultado.</param>
    public async Task CheckAsync(bool manual)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            if (Repository is not { } repo || Environment.ProcessPath is not { } exePath)
            {
                if (manual) TrayIcon.Notify("Atualizações", "Atualização automática não configurada nesta versão.");
                return;
            }

            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Assistente-Atualizador", CurrentVersionText));
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var response = await http.GetAsync($"https://api.github.com/repos/{repo}/releases/latest");
            if (!response.IsSuccessStatusCode)
            {
                if (manual) TrayIcon.Notify("Atualizações", "Ainda não há versões publicadas.");
                return;
            }

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = json.RootElement;
            var tag = root.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest) || Normalize(latest) <= Normalize(CurrentVersion))
            {
                if (manual) TrayIcon.Notify("Atualizações", $"Você já está na versão mais recente ({CurrentVersionText}).");
                return;
            }

            var assetName = IsLightBuild ? LightAssetName : FullAssetName;
            var url = root.GetProperty("assets").EnumerateArray()
                .Where(a => a.GetProperty("name").GetString() == assetName)
                .Select(a => a.GetProperty("browser_download_url").GetString())
                .FirstOrDefault();
            if (url is null)
            {
                if (manual) TrayIcon.Notify("Atualizações", $"A versão {tag} não tem o arquivo {assetName}.");
                return;
            }

            TrayIcon.Notify("Atualizando o Assistente", $"Baixando a versão {tag}…");
            var newPath = exePath + ".new";
            await using (var download = await http.GetStreamAsync(url))
            await using (var file = File.Create(newPath))
            {
                await download.CopyToAsync(file);
            }

            _pendingNewPath = newPath;
            _pendingVersion = latest;
            TryApplyPending();
        }
        catch (Exception ex)
        {
            if (manual) TrayIcon.Notify("Atualizações", $"Não foi possível atualizar: {ex.Message}");
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Troca o .exe e reinicia, mas só com o painel recolhido para não atrapalhar.</summary>
    private void TryApplyPending()
    {
        if (_pendingNewPath is null || Environment.ProcessPath is not { } exePath) return;

        if (MainWindow.Current is { IsExpanded: true })
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
            TrayIcon.Notify("Atualizações", $"Não foi possível instalar a versão {_pendingVersion}: {ex.Message}");
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
