using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MinecraftServerBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OccurredUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RequesterUserId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    ConfirmerUserId = table.Column<ulong>(type: "INTEGER", nullable: true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Args = table.Column<string>(type: "TEXT", nullable: true),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Detail = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConversationMessages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GuildId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    ChannelId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    AuthorId = table.Column<ulong>(type: "INTEGER", nullable: true),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    ToolName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlayerSessions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlayerName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    JoinedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LeftUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DurationSeconds = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerSessions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_OccurredUtc",
                table: "AuditEntries",
                column: "OccurredUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationMessages_ChannelId_CreatedUtc",
                table: "ConversationMessages",
                columns: new[] { "ChannelId", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerSessions_PlayerName_JoinedUtc",
                table: "PlayerSessions",
                columns: new[] { "PlayerName", "JoinedUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEntries");

            migrationBuilder.DropTable(
                name: "ConversationMessages");

            migrationBuilder.DropTable(
                name: "PlayerSessions");
        }
    }
}
