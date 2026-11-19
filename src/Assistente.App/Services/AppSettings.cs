using System;
using System.IO;
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

    private const int CurrentVersion = 2;

    /// <summary>Versão do arquivo, para migrar preferências antigas.</summary>
    public int Version { get; set; }

    /// <summary>Borda da tela em que o widget fica.</summary>
    public DockEdge Edge { get; set; } = DockEdge.Top;

    /// <summary>Posição ao longo da borda (0 = início, 0,5 = centro, 1 = fim).</summary>
    public double Position { get; set; } = 0.5;

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
                    // v2: o padrão passou a ser o topo da tela, centralizado.
                    settings.Edge = DockEdge.Top;
                    settings.Position = 0.5;
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
