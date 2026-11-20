using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Assistente.App.Services;

public sealed record ChatMessage(string Role, string Content);

/// <summary>
/// Conversa com uma IA.
/// - "gratuito": Pollinations (text.pollinations.ai), sem conta nem chave.
/// - "personalizado": qualquer API compatível com OpenAI (Groq, OpenRouter, OpenAI…), com a chave do usuário.
/// </summary>
public sealed class ChatService
{
    private const string SystemPrompt =
        "Você é o assistente de um aplicativo para Windows. Responda sempre em português do Brasil, " +
        "de forma clara, amigável e objetiva. Use listas curtas quando ajudar. Não use tabelas.";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Assistente", UpdateService.CurrentVersionText));
        return http;
    }

    public async Task<string> AskAsync(IReadOnlyList<ChatMessage> history, AppSettings settings, CancellationToken ct)
    {
        var messages = new List<object> { new { role = "system", content = SystemPrompt } };
        // Mantém o contexto recente para não estourar limites de tamanho.
        messages.AddRange(history.TakeLast(20).Select(m => new { role = m.Role, content = m.Content }));

        if (settings.ChatProvider == "personalizado")
        {
            var key = settings.ChatApiKey;
            if (string.IsNullOrWhiteSpace(key))
                throw new InvalidOperationException("Cole sua chave da API em Configurações → Chat IA, ou volte para o modo gratuito.");
            var url = settings.ChatBaseUrl.TrimEnd('/') + "/chat/completions";
            return await PostOpenAiAsync(url, settings.ChatModel, messages, key, ct);
        }

        try
        {
            return await PostOpenAiAsync("https://text.pollinations.ai/openai", "openai", messages, null, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Plano B do serviço gratuito: endpoint simples por GET.
            return await PollinationsGetAsync(history, ct);
        }
    }

    private static async Task<string> PostOpenAiAsync(string url, string model, List<object> messages, string? key, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new { model, messages, temperature = 0.7, referrer = "assistente-windows", @private = true }),
        };
        if (key is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

        using var response = await Http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(FriendlyError((int)response.StatusCode, body));

        using var json = JsonDocument.Parse(body);
        var content = json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        return string.IsNullOrWhiteSpace(content) ? "(resposta vazia)" : content.Trim();
    }

    private static async Task<string> PollinationsGetAsync(IReadOnlyList<ChatMessage> history, CancellationToken ct)
    {
        var transcript = new StringBuilder();
        foreach (var m in history.TakeLast(8))
            transcript.AppendLine((m.Role == "user" ? "Usuário: " : "Assistente: ") + m.Content);
        transcript.Append("Assistente:");

        var url = "https://text.pollinations.ai/" + Uri.EscapeDataString(transcript.ToString()) +
                  "?model=openai&private=true&referrer=assistente-windows&system=" + Uri.EscapeDataString(SystemPrompt);
        using var response = await Http.GetAsync(url, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(FriendlyError((int)response.StatusCode, text));
        return text.Trim();
    }

    private static string FriendlyError(int status, string body) => status switch
    {
        401 or 403 => "A chave da API foi recusada. Confira em Configurações → Chat IA.",
        429 => "Muitas mensagens em pouco tempo. Espere alguns segundos e tente de novo.",
        >= 500 => "O serviço de IA está fora do ar ou sobrecarregado agora. Tente de novo em instantes.",
        _ => $"O serviço de IA respondeu com erro {status}.",
    };
}
