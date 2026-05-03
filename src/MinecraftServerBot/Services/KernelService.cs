using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using MinecraftServerBot.Configuration;
using MinecraftServerBot.Plugins;

namespace MinecraftServerBot.Services;

public sealed class KernelService
{
    private readonly Kernel _kernel;
    private readonly IChatCompletionService _chat;
    private readonly IOptionsMonitor<LlmOptions> _options;
    private readonly ILogger<KernelService> _logger;

    public KernelService(
        IOptions<LlmOptions> initialOptions,
        IOptionsMonitor<LlmOptions> options,
        ILogger<KernelService> logger,
        ServerStatusPlugin statusPlugin)
    {
        _options = options;
        _logger = logger;

        var initial = initialOptions.Value;
        var builder = Kernel.CreateBuilder();

        builder.AddOpenAIChatCompletion(
            modelId: initial.Model,
            apiKey: initial.ApiKey,
            endpoint: new Uri(initial.Endpoint));

        builder.Plugins.AddFromObject(statusPlugin, "ServerStatus");

        _kernel = builder.Build();
        _chat = _kernel.GetRequiredService<IChatCompletionService>();

        _logger.LogInformation(
            "Kernel initialized with OpenRouter {Model} and {Plugins} plugins",
            initial.Model,
            _kernel.Plugins.Count);
    }

    public Kernel Kernel => _kernel;

    public async Task<ChatMessageContent> CompleteAsync(ChatHistory history, CancellationToken ct = default)
    {
        var opts = _options.CurrentValue;
        var settings = new PromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
            ExtensionData = new Dictionary<string, object>
            {
                ["temperature"] = opts.Temperature,
                ["max_tokens"] = opts.MaxOutputTokens,
            },
        };

        var response = await _chat.GetChatMessageContentAsync(history, settings, _kernel, ct);

        LogTokenUsage(response, opts.Model);
        return response;
    }

    private void LogTokenUsage(ChatMessageContent response, string model)
    {
        if (response.Metadata is null)
        {
            return;
        }

        int? inputTokens = null;
        int? outputTokens = null;
        if (response.Metadata.TryGetValue("Usage", out var usageObj) && usageObj is not null)
        {
            var t = usageObj.GetType();
            inputTokens = (int?)t.GetProperty("InputTokenCount")?.GetValue(usageObj);
            outputTokens = (int?)t.GetProperty("OutputTokenCount")?.GetValue(usageObj);
        }

        _logger.LogInformation(
            "LLM completion model={Model} input={Input} output={Output}",
            model,
            inputTokens,
            outputTokens);
    }
}
