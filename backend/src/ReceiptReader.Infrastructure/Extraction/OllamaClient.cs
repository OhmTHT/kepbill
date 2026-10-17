using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace ReceiptReader.Infrastructure.Extraction;

/// <summary>Minimal client for Ollama's /api/chat (non-streaming). Docs: https://github.com/ollama/ollama/blob/main/docs/api.md</summary>
public sealed class OllamaClient(HttpClient http, IOptions<ExtractionOptions> options)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public sealed record Message(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content,
        [property: JsonPropertyName("images")] IReadOnlyList<string>? Images = null);

    private sealed record ChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<Message> Messages,
        [property: JsonPropertyName("stream")] bool Stream,
        [property: JsonPropertyName("format")] JsonNode? Format,
        [property: JsonPropertyName("options")] JsonObject Options,
        [property: JsonPropertyName("keep_alive")] string KeepAlive,
        [property: JsonPropertyName("think"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Think);

    private sealed record ChatResponse(
        [property: JsonPropertyName("model")] string? Model,
        [property: JsonPropertyName("message")] Message? Message,
        [property: JsonPropertyName("done_reason")] string? DoneReason);

    /// <param name="format">A JSON schema (structured output), or null for free text.</param>
    public async Task<string> ChatAsync(string model, IReadOnlyList<Message> messages, JsonNode? format, CancellationToken ct)
    {
        var opt = options.Value;
        var req = new ChatRequest(
            model, messages, Stream: false, format,
            new JsonObject { ["temperature"] = opt.Temperature, ["num_ctx"] = opt.NumCtx },
            opt.KeepAlive, opt.Think);

        using var resp = await http.PostAsJsonAsync("/api/chat", req, Json, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"Ollama returned {(int)resp.StatusCode} for model '{model}': {Truncate(body, 500)}" +
                (body.Contains("not found", StringComparison.OrdinalIgnoreCase) ? $" — run: ollama pull {model}" : ""));
        }

        var chat = await resp.Content.ReadFromJsonAsync<ChatResponse>(Json, ct)
                   ?? throw new InvalidOperationException("Empty response from Ollama");
        var content = chat.Message?.Content;
        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException($"Ollama model '{model}' returned no content (done_reason={chat.DoneReason}).");
        if (chat.DoneReason == "length")
            throw new InvalidOperationException($"Output was cut off at the context limit (num_ctx={opt.NumCtx}). Increase Extraction:NumCtx.");
        return content;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
