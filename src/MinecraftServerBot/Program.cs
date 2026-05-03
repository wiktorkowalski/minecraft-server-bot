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

    app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

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
