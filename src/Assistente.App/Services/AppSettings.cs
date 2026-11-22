using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Assistente.App.Services;

public enum DockEdge { Top, Right, Left }

/// <summary>Preferências salvas em %AppData%\Assistente\settings.json.</summary>
public sealed class AppSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Assistente", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private const int CurrentVersion = 3;

    /// <summary>Versão do arquivo, para migrar preferências antigas.</summary>
    public int Version { get; set; }

    /// <summary>Borda da tela em que o widget fica.</summary>
    public DockEdge Edge { get; set; } = DockEdge.Top;

    /// <summary>Posição ao longo da borda (0 = início, 0,5 = centro, 1 = fim).</summary>
    public double Position { get; set; } = 0.5;

    // ---------- Personalização ----------

    /// <summary>Cor de destaque em hexadecimal (#RRGGBB).</summary>
    public string AccentColor { get; set; } = "#4F8CFF";

    /// <summary>Opacidade do fundo do painel (0,6 a 1).</summary>
    public double PanelOpacity { get; set; } = 0.95;

    /// <summary>Tamanho do painel em fração da tela (0,3 a 0,5).</summary>
    public double PanelSize { get; set; } = 0.5;

    public bool Animations { get; set; } = true;

    /// <summary>Tema (cor de fundo das telas). Ver ThemeService.Themes.</summary>
    public string Theme { get; set; } = "Escuro";

    /// <summary>Opacidade das janelas (configurações) de 0,6 a 1.</summary>
    public double WindowOpacity { get; set; } = 0.97;

    /// <summary>Avatar da alça (id em Avatars). Vazio = setinha.</summary>
    public string? Avatar { get; set; }

    // ---------- Chat IA ----------

    /// <summary>"gratuito" (sem cadastro) ou "personalizado" (compatível com OpenAI).</summary>
    public string ChatProvider { get; set; } = "gratuito";

    public string ChatBaseUrl { get; set; } = "https://api.groq.com/openai/v1";

    public string ChatModel { get; set; } = "llama-3.3-70b-versatile";

    /// <summary>Chave da API criptografada com a conta do Windows (DPAPI).</summary>
    public string? ChatApiKeyProtected { get; set; }

    [JsonIgnore]
    public string? ChatApiKey
    {
        get
        {
            if (string.IsNullOrEmpty(ChatApiKeyProtected)) return null;
            try
            {
                var bytes = ProtectedData.Unprotect(Convert.FromBase64String(ChatApiKeyProtected), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch (Exception)
            {
                return null;
            }
        }
        set => ChatApiKeyProtected = string.IsNullOrWhiteSpace(value)
            ? null
            : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value.Trim()), null, DataProtectionScope.CurrentUser));
    }

    /// <summary>Tempo até recolher depois que o mouse sai (ms).</summary>
    public int CloseDelayMs { get; set; } = 450;

    /// <summary>Procurar atualizações sozinho ao abrir e a cada 6 horas.</summary>
    public bool AutoUpdate { get; set; } = true;

    /// <summary>Mostrar o mini widget de música ao lado do avatar quando algo estiver tocando.</summary>
    public bool MusicWidget { get; set; } = true;

    /// <summary>Mostrar o mini widget de timer/cronômetro ao lado do avatar.</summary>
    public bool TimeWidget { get; set; } = true;

    // ---------- Dock estilo Mac ----------

    public bool DockEnabled { get; set; }
    public double DockIconSize { get; set; } = 48;

    /// <summary>Quanto o ícone cresce com o mouse em cima (1 = sem zoom).</summary>
    public double DockMagnify { get; set; } = 1.7;
    /// <summary>Antigo (v0.8). Substituído por DockHideMode.</summary>
    public bool DockAutoHide { get; set; }

    /// <summary>"janela" = some quando uma janela encosta no dock (como no Mac); "sempre"; "nunca".</summary>
    public string DockHideMode { get; set; } = "janela";

    /// <summary>Esconde de vez a barra de tarefas do Windows enquanto o dock estiver ligado.</summary>
    public bool DockHideTaskbar { get; set; } = true;

    /// <summary>Mostrar o avatar só quando não houver janela embaixo dele (volta ao encostar o mouse no topo).</summary>
    public bool AvatarAutoHide { get; set; } = true;
    public bool DockShowRunning { get; set; } = true;

    /// <summary>Programas fixados (caminho do .exe ou atalho .lnk). null = lista padrão.</summary>
    public System.Collections.Generic.List<string>? DockPinned { get; set; }

    /// <summary>Estado original do "ocultar automaticamente" da barra do Windows, para devolver depois.</summary>
    public bool? DockTaskbarWasAutoHide { get; set; }

    /// <summary>Última ferramenta aberta.</summary>
    public string? LastTool { get; set; }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new AppSettings();
                if (settings.Version < CurrentVersion)
                {
                    if (settings.Version < 2)
                    {
                        // v2: o padrão passou a ser o topo da tela, centralizado.
                        settings.Edge = DockEdge.Top;
                        settings.Position = 0.5;
                    }
                    // v3: dock como no Mac (some quando uma janela encosta, sem a barra do Windows,
                    // mostrando fixados e abertos).
                    settings.DockHideMode = "janela";
                    settings.DockHideTaskbar = true;
                    settings.DockShowRunning = true;
                    settings.Version = CurrentVersion;
                    settings.Save();
                }
                return settings;
            }
        }
        catch (Exception)
        {
            // Arquivo corrompido: volta ao padrão.
        }
        return new AppSettings { Version = CurrentVersion };
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception)
        {
            // Preferências não são críticas.
        }
    }
}
