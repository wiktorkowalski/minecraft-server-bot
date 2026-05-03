using Microsoft.EntityFrameworkCore;
using MinecraftServerBot.Data.Entities;

namespace MinecraftServerBot.Data;

public sealed class McBotDbContext : DbContext
{
    public McBotDbContext(DbContextOptions<McBotDbContext> options)
        : base(options)
    {
    }

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    public DbSet<ConversationMessage> ConversationMessages => Set<ConversationMessage>();

    public DbSet<PlayerSession> PlayerSessions => Set<PlayerSession>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AuditEntry>(b =>
        {
            b.HasKey(e => e.Id);
            b.HasIndex(e => e.OccurredUtc);
            b.Property(e => e.Action).HasMaxLength(128).IsRequired();
            b.Property(e => e.Source).HasConversion<string>().HasMaxLength(32);
            b.Property(e => e.Outcome).HasConversion<string>().HasMaxLength(32);
        });

        modelBuilder.Entity<ConversationMessage>(b =>
        {
            b.HasKey(e => e.Id);
            b.HasIndex(e => new { e.ChannelId, e.CreatedUtc });
            b.Property(e => e.Role).HasConversion<string>().HasMaxLength(32);
            b.Property(e => e.ToolName).HasMaxLength(128);
        });

        modelBuilder.Entity<PlayerSession>(b =>
        {
            b.HasKey(e => e.Id);
            b.HasIndex(e => new { e.PlayerName, e.JoinedUtc });
            b.Property(e => e.PlayerName).HasMaxLength(64).IsRequired();
        });
    }
}
