using DotNetEnv;
using Microsoft.EntityFrameworkCore;
using MinecraftServerBot.Configuration;
using MinecraftServerBot.Data;
using MinecraftServerBot.Minecraft;
using MinecraftServerBot.Plugins;
using MinecraftServerBot.Services;
using Serilog;

Env.TraversePath().Load();

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting MinecraftServerBot...");

    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddSerilog((services, lc) => lc
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services));

    builder.Services.AddOptions<DiscordOptions>()
        .Bind(builder.Configuration.GetSection(DiscordOptions.SectionName));

    builder.Services.AddOptions<McServerOptions>()
        .Bind(builder.Configuration.GetSection(McServerOptions.SectionName));

    builder.Services.AddOptions<LlmOptions>()
        .Bind(builder.Configuration.GetSection(LlmOptions.SectionName));

    builder.Services.AddOptions<ConversationOptions>()
        .Bind(builder.Configuration.GetSection(ConversationOptions.SectionName));

    builder.Services.AddOptions<PollerOptions>()
        .Bind(builder.Configuration.GetSection(PollerOptions.SectionName));

    builder.Services.AddOptions<AnnouncementOptions>()
        .Bind(builder.Configuration.GetSection(AnnouncementOptions.SectionName));

    builder.Services.AddOptions<PresenceOptions>()
        .Bind(builder.Configuration.GetSection(PresenceOptions.SectionName));

    builder.Services.AddOptions<ExecOptions>()
        .Bind(builder.Configuration.GetSection(ExecOptions.SectionName));

    builder.Services.AddOptions<ConfirmationOptions>()
        .Bind(builder.Configuration.GetSection(ConfirmationOptions.SectionName));

    builder.Services.AddOptions<HealthOptions>()
        .Bind(builder.Configuration.GetSection(HealthOptions.SectionName));

    builder.Services.AddOptions<AuditOptions>()
        .Bind(builder.Configuration.GetSection(AuditOptions.SectionName));

    builder.Services.AddOptions<OpenTelemetryOptions>()
        .Bind(builder.Configuration.GetSection(OpenTelemetryOptions.SectionName));

    var dataPath = Environment.GetEnvironmentVariable("DATA_PATH") ?? "data";
    Directory.CreateDirectory(dataPath);
    var connectionString = builder.Configuration.GetConnectionString("Database")
        ?? $"Data Source={Path.Combine(dataPath, "mc-bot.db")}";

    builder.Services.AddDbContextFactory<McBotDbContext>(options =>
        options.UseSqlite(connectionString));

    builder.Services.AddSingleton<IRconClient, RconClient>();
    builder.Services.AddSingleton<ISlpClient, SlpClient>();
    builder.Services.AddSingleton<McServerActions>();

    builder.Services.AddSingleton<McStatusPollerService>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<McStatusPollerService>());

    builder.Services.AddSingleton<RateLimitService>();
    builder.Services.AddSingleton<AuditService>();
    builder.Services.AddSingleton<ConfirmationService>();
    builder.Services.AddSingleton<ConversationService>();
    builder.Services.AddSingleton<ServerStatusPlugin>();
    builder.Services.AddSingleton<KernelService>();
    builder.Services.AddSingleton<DiscordBotService>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<DiscordBotService>());

    builder.Services.AddHostedService<PresenceUpdaterService>();
    builder.Services.AddHostedService<AnnouncementService>();

    var app = builder.Build();

    var dbFactory = app.Services.GetRequiredService<IDbContextFactory<McBotDbContext>>();
    await using (var db = await dbFactory.CreateDbContextAsync())
    {
        await db.Database.MigrateAsync();
    }

    app.MapGet("/health", (IServiceProvider sp) =>
    {
        var bot = sp.GetRequiredService<DiscordBotService>();
        var poller = sp.GetRequiredService<McStatusPollerService>();
        var healthOpts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthOptions>>().Value;

        var lastPoll = poller.LastSuccessfulPollUtc;
        var pollAge = lastPoll is null ? (TimeSpan?)null : DateTime.UtcNow - lastPoll.Value;
        var pollFresh = pollAge is { } a && a < TimeSpan.FromMinutes(2);

        var discord = new
        {
            ok = bot.IsReady,
            detail = bot.IsReady ? "connected" : "not yet ready",
        };
        var slp = new
        {
            ok = poller.LastStatus.Online,
            detail = poller.LastStatus.Online
                ? $"online {poller.LastStatus.OnlinePlayers}/{poller.LastStatus.MaxPlayers}"
                : poller.LastStatus.Error ?? "down",
        };
        var rcon = new
        {
            ok = pollFresh,
            detail = lastPoll is null
                ? "no successful poll yet"
                : $"last successful poll {pollAge!.Value.TotalSeconds:F0}s ago",
        };

        var anyDown = !discord.ok || !slp.ok || !rcon.ok;
        var unhealthy = healthOpts.StrictMode ? anyDown : !discord.ok;

        var body = new
        {
            status = anyDown ? "degraded" : "ok",
            components = new { discord, rcon, slp },
            lastPollUtc = lastPoll,
        };

        return unhealthy
            ? Results.Json(body, statusCode: 503)
            : Results.Ok(body);
    });

    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

return 0;
