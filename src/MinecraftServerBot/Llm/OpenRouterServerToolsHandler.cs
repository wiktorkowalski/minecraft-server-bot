using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MinecraftServerBot.Configuration;

namespace MinecraftServerBot.Llm;

/// <summary>
/// Injects OpenRouter server tools (openrouter:web_search, openrouter:web_fetch) into the
/// outgoing chat completion request body, alongside SK's auto-generated KernelFunction tools.
/// OpenRouter executes server tools server-side and returns the final assistant message;
/// SK never has to know they exist.
/// </summary>
public sealed class OpenRouterServerToolsHandler : DelegatingHandler
{
    private static readonly string[] ServerToolTypes =
    {
        "openrouter:web_search",
        "openrouter:web_fetch",
    };

    private readonly IOptionsMonitor<LlmOptions> _options;
    private readonly ILogger<OpenRouterServerToolsHandler> _logger;

    public OpenRouterServerToolsHandler(
        IOptionsMonitor<LlmOptions> options,
        ILogger<OpenRouterServerToolsHandler> logger)
    {
        _options = options;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!_options.CurrentValue.EnableWebTools)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var path = request.RequestUri?.AbsolutePath;
        var isChatCompletion = request.Method == HttpMethod.Post
            && path is not null
            && path.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
            && request.Content is not null;

        if (!isChatCompletion)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        try
        {
            var raw = await request.Content!.ReadAsStringAsync(cancellationToken);
            var modified = InjectServerTools(raw);
            request.Content = new StringContent(modified, Encoding.UTF8, "application/json");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to inject OpenRouter server tools; forwarding request unmodified");
        }

        return await base.SendAsync(request, cancellationToken);
    }

    private static string InjectServerTools(string body)
    {
        using var doc = JsonDocument.Parse(body);
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartObject();
            var wroteTools = false;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.NameEquals("tools"))
                {
                    WriteMergedTools(writer, prop.Value);
                    wroteTools = true;
                }
                else
                {
                    prop.WriteTo(writer);
                }
            }

            if (!wroteTools)
            {
                WriteMergedTools(writer, default);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static void WriteMergedTools(Utf8JsonWriter writer, JsonElement existing)
    {
        writer.WritePropertyName("tools");
        writer.WriteStartArray();

        if (existing.ValueKind == JsonValueKind.Array)
        {
            foreach (var t in existing.EnumerateArray())
            {
                t.WriteTo(writer);
            }
        }

        foreach (var serverTool in ServerToolTypes)
        {
            writer.WriteStartObject();
            writer.WriteString("type", serverTool);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }
}
