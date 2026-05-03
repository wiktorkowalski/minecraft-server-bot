using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MinecraftServerBot.Data;
using MinecraftServerBot.Data.Entities;

namespace MinecraftServerBot.Services;

public sealed class AuditService
{
    private readonly IDbContextFactory<McBotDbContext> _dbFactory;
    private readonly ILogger<AuditService> _logger;

    public AuditService(IDbContextFactory<McBotDbContext> dbFactory, ILogger<AuditService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task WriteAsync(
        ulong requesterUserId,
        AuditSource source,
        string action,
        AuditOutcome outcome,
        string? args = null,
        string? detail = null,
        ulong? confirmerUserId = null,
        CancellationToken ct = default)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            db.AuditEntries.Add(new AuditEntry
            {
                OccurredUtc = DateTime.UtcNow,
                RequesterUserId = requesterUserId,
                ConfirmerUserId = confirmerUserId,
                Source = source,
                Action = action,
                Args = args,
                Outcome = outcome,
                Detail = detail,
            });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to write audit row: {Action} by {User} via {Source}",
                action,
                requesterUserId,
                source);
        }
    }
}
