using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace Assistente.App.Services;

/// <summary>Um acesso remoto cadastrado (Área de Trabalho Remota, TightVNC ou outro app).</summary>
public sealed class RemoteConnection
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";

    /// <summary>"rdp" (Área de Trabalho Remota), "vnc" (TightVNC) ou "outro".</summary>
    public string Kind { get; set; } = "rdp";

    public string Host { get; set; } = "";
    public int? Port { get; set; }
    public string User { get; set; } = "";

    /// <summary>Senha protegida com DPAPI (só abre neste usuário do Windows).</summary>
    public string? PasswordProtected { get; set; }

    /// <summary>Programa usado (VNC ou "outro"); vazio = procura sozinho.</summary>
    public string Program { get; set; } = "";

    /// <summary>Argumentos do "outro" app; aceita {host}, {port}, {user}, {password}.</summary>
    public string Args { get; set; } = "";

    [JsonIgnore]
    public string KindLabel => Kind switch
    {
        "rdp" => "Área de Trabalho Remota",
        "vnc" => "TightVNC",
        _ => string.IsNullOrWhiteSpace(Program) ? "Outro app" : Path.GetFileNameWithoutExtension(Program),
    };

    [JsonIgnore]
    public string Address => Port is { } p ? $"{Host}:{p}" : Host;

    [JsonIgnore]
    public string Password
    {
        get
        {
            if (string.IsNullOrEmpty(PasswordProtected)) return "";
            try
            {
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(PasswordProtected), null, DataProtectionScope.CurrentUser));
            }
            catch (Exception)
            {
                return "";
            }
        }
        set => PasswordProtected = string.IsNullOrEmpty(value)
            ? null
            : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
    }
}

/// <summary>Abre as conexões remotas com o programa certo.</summary>
public static class RemoteAccess
{
    /// <summary>Conecta. Retorna uma mensagem de erro (em português) ou null se deu certo.</summary>
    public static string? Connect(RemoteConnection c)
    {
        if (string.IsNullOrWhiteSpace(c.Host) && c.Kind != "outro") return "Falta o endereço do computador.";
        try
        {
            return c.Kind switch
            {
                "rdp" => ConnectRdp(c),
                "vnc" => ConnectVnc(c),
                _ => ConnectOther(c),
            };
        }
        catch (Exception ex)
        {
            return "Não deu para abrir: " + ex.Message;
        }
    }

    private static string? ConnectRdp(RemoteConnection c)
    {
        // Com senha: guarda no Gerenciador de Credenciais do Windows (é o que o mstsc usa).
        var password = c.Password;
        if (!string.IsNullOrEmpty(c.User) && !string.IsNullOrEmpty(password))
            RunHidden("cmdkey.exe", $"/generic:TERMSRV/{c.Host} /user:{Quote(c.User)} /pass:{Quote(password)}");

        // Abre o app "Conexão de Área de Trabalho Remota" direto no computador, como se você
        // digitasse o endereço e clicasse em Conectar (sem arquivo .rdp, que mostra aviso de editor).
        Process.Start(new ProcessStartInfo("mstsc.exe", $"/v:{c.Address}") { UseShellExecute = true });
        return null;
    }

    private static string? ConnectVnc(RemoteConnection c)
    {
        var viewer = File.Exists(c.Program) ? c.Program : FindTightVnc();
        if (viewer is null) return "Não achei o TightVNC Viewer. Instale o TightVNC ou indique o programa (tvnviewer.exe) no cadastro.";
        var args = new StringBuilder($"-host={c.Host}");
        if (c.Port is { } port) args.Append($" -port={port}");
        var password = c.Password;
        if (!string.IsNullOrEmpty(password)) args.Append($" -password={Quote(password)}");
        Process.Start(new ProcessStartInfo(viewer, args.ToString()) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(viewer) ?? "" });
        return null;
    }

    private static string? ConnectOther(RemoteConnection c)
    {
        if (string.IsNullOrWhiteSpace(c.Program)) return "Escolha o programa de acesso remoto no cadastro.";
        var args = (c.Args ?? "")
            .Replace("{host}", c.Host)
            .Replace("{port}", c.Port?.ToString() ?? "")
            .Replace("{user}", c.User)
            .Replace("{password}", c.Password);
        if (string.IsNullOrWhiteSpace(c.Args) && !string.IsNullOrWhiteSpace(c.Host)) args = c.Host;
        Process.Start(new ProcessStartInfo(c.Program, args) { UseShellExecute = true });
        return null;
    }

    public static string? FindTightVnc()
    {
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
        {
            if (string.IsNullOrEmpty(root)) continue;
            var path = Path.Combine(root, "TightVNC", "tvnviewer.exe");
            if (File.Exists(path)) return path;
        }
        return null;
    }

    private static string Quote(string s) => "\"" + s.Replace("\"", "\\\"") + "\"";

    private static void RunHidden(string exe, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(exe, args) { CreateNoWindow = true, UseShellExecute = false });
            p?.WaitForExit(3000);
        }
        catch (Exception)
        {
            // Sem o cmdkey o mstsc só pede a senha.
        }
    }
}
