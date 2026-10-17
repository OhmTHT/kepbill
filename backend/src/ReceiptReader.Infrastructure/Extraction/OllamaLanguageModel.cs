using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using ReceiptReader.Application.Abstractions;

namespace ReceiptReader.Infrastructure.Extraction;

/// <summary>The product code suggestions, answered by the Ollama model in Extraction:AssistModel.</summary>
public sealed class OllamaLanguageModel(OllamaClient ollama, IOptions<ExtractionOptions> options) : ILanguageModel
{
    public Task<string> CompleteJsonAsync(string system, string user, JsonObject schema, CancellationToken ct)
    {
        var opt = options.Value;
        var model = string.IsNullOrWhiteSpace(opt.AssistModel) ? opt.VisionModel : opt.AssistModel;
        return ollama.ChatAsync(model, [new("system", system), new("user", user)], schema, ct);
    }
}
