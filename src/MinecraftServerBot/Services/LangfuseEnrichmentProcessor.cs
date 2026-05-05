using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenTelemetry;

namespace MinecraftServerBot.Services;

/// <summary>
/// Enriches Semantic Kernel spans for Langfuse. SK puts message content in OTel
/// events (gen_ai.user.message, gen_ai.choice, gen_ai.tool.message,
/// gen_ai.tool.result) instead of attributes; Langfuse renders input/output from
/// langfuse.observation.input/output attributes — without this processor traces
/// arrive but the UI shows raw event JSON instead of extracted message content.
/// </summary>
public sealed class LangfuseEnrichmentProcessor : BaseProcessor<Activity>
{
    private readonly ILogger<LangfuseEnrichmentProcessor> _logger;

    public LangfuseEnrichmentProcessor(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<LangfuseEnrichmentProcessor>();
    }

    public override void OnEnd(Activity activity)
    {
        try
        {
            if (activity.Source.Name.StartsWith("Microsoft.SemanticKernel", StringComparison.Ordinal))
            {
                var operation = activity.GetTagItem("gen_ai.operation.name")?.ToString();

                if (operation == "execute_tool")
                {
                    var args = ExtractToolContent(activity, "gen_ai.tool.message");
                    if (!string.IsNullOrEmpty(args))
                    {
                        activity.SetTag("langfuse.observation.input", args);
                    }

                    var result = ExtractToolContent(activity, "gen_ai.tool.result");
                    if (!string.IsNullOrEmpty(result))
                    {
                        activity.SetTag("langfuse.observation.output", result);
                    }
                }
                else
                {
                    var input = ExtractContent(activity, "gen_ai.user.message");
                    if (!string.IsNullOrEmpty(input))
                    {
                        activity.SetTag("langfuse.observation.input", input);
                    }

                    var output = ExtractContent(activity, "gen_ai.choice");
                    if (!string.IsNullOrEmpty(output))
                    {
                        activity.SetTag("langfuse.observation.output", output);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enriching span {ActivityName} for Langfuse", activity.DisplayName);
        }

        base.OnEnd(activity);
    }

    private static string? ExtractContent(Activity activity, string eventName)
    {
        string? lastContent = null;

        foreach (var evt in activity.Events)
        {
            if (evt.Name != eventName)
            {
                continue;
            }

            foreach (var tag in evt.Tags)
            {
                if (tag.Key != "gen_ai.event.content")
                {
                    continue;
                }

                var json = tag.Value?.ToString();
                if (string.IsNullOrEmpty(json))
                {
                    continue;
                }

                try
                {
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("content", out var c))
                    {
                        lastContent = c.GetString();
                    }
                    else if (doc.RootElement.TryGetProperty("message", out var m)
                        && m.TryGetProperty("content", out var mc))
                    {
                        lastContent = mc.GetString();
                    }
                }
                catch (JsonException)
                {
                    lastContent = json;
                }
            }
        }

        return lastContent;
    }

    private static string? ExtractToolContent(Activity activity, string eventName)
    {
        foreach (var evt in activity.Events)
        {
            if (evt.Name != eventName)
            {
                continue;
            }

            foreach (var tag in evt.Tags)
            {
                if (tag.Key != "gen_ai.event.content")
                {
                    continue;
                }

                var json = tag.Value?.ToString();
                if (string.IsNullOrEmpty(json))
                {
                    return null;
                }

                try
                {
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("content", out var c))
                    {
                        return c.GetString();
                    }

                    if (doc.RootElement.TryGetProperty("arguments", out var args))
                    {
                        return args.GetString();
                    }

                    return json;
                }
                catch (JsonException)
                {
                    return json;
                }
            }
        }

        return null;
    }
}
