using System.Diagnostics;
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
        IHttpClientFactory httpClientFactory,
        ILogger<KernelService> logger,
        ServerStatusPlugin statusPlugin,
        ServerControlPlugin controlPlugin,
        ServerExecPlugin execPlugin)
    {
        _options = options;
        _logger = logger;

        var initial = initialOptions.Value;
        var httpClient = httpClientFactory.CreateClient("openrouter");
        var builder = Kernel.CreateBuilder();

        builder.AddOpenAIChatCompletion(
            modelId: initial.Model,
            apiKey: initial.ApiKey,
            endpoint: new Uri(initial.Endpoint),
            httpClient: httpClient);

        builder.Plugins.AddFromObject(statusPlugin, "ServerStatus");
        builder.Plugins.AddFromObject(controlPlugin, "ServerControl");
        builder.Plugins.AddFromObject(execPlugin, "ServerExec");

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

        var sw = Stopwatch.StartNew();
        var response = await _chat.GetChatMessageContentAsync(history, settings, _kernel, ct);
        sw.Stop();

        LogTokenUsage(response, opts.Model, sw.Elapsed, mode: "chat");
        return response;
    }

    public async Task<string> OneShotAsync(
        string systemPrompt,
        string userPrompt,
        int maxTokens,
        CancellationToken ct = default)
    {
        var opts = _options.CurrentValue;
        var settings = new PromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.None(),
            ExtensionData = new Dictionary<string, object>
            {
                ["temperature"] = opts.Temperature,
                ["max_tokens"] = maxTokens,
            },
        };

        var history = new ChatHistory();
        history.AddSystemMessage(systemPrompt);
        history.AddUserMessage(userPrompt);

        var sw = Stopwatch.StartNew();
        var response = await _chat.GetChatMessageContentAsync(history, settings, _kernel, ct);
        sw.Stop();

        LogTokenUsage(response, opts.Model, sw.Elapsed, mode: "oneshot");
        return response.Content ?? string.Empty;
    }

    private void LogTokenUsage(ChatMessageContent response, string model, TimeSpan elapsed, string mode)
    {
        int? inputTokens = null;
        int? outputTokens = null;
        if (response.Metadata is not null
            && response.Metadata.TryGetValue("Usage", out var usageObj)
            && usageObj is not null)
        {
            var t = usageObj.GetType();
            inputTokens = (int?)t.GetProperty("InputTokenCount")?.GetValue(usageObj);
            outputTokens = (int?)t.GetProperty("OutputTokenCount")?.GetValue(usageObj);
        }

        var ms = (int)elapsed.TotalMilliseconds;
        var likelyWebTool = ms > 5000 || (inputTokens is { } it && it > 3000);

        _logger.LogInformation(
            "LLM completion mode={Mode} model={Model} input={Input} output={Output} duration={DurationMs}ms web_tool_likely={WebToolLikely}",
            mode,
            model,
            inputTokens,
            outputTokens,
            ms,
            likelyWebTool);
    }
}
