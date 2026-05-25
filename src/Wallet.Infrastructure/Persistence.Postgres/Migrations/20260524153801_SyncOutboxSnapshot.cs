using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wallet.Infrastructure.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class SyncOutboxSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wallet_outbox",
                columns: table => new
                {
                    event_id = table.Column<string>(type: "text", nullable: false),
                    player_id = table.Column<string>(type: "text", nullable: false),
                    event_type = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    balance_after = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    occurred_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wallet_outbox", x => x.event_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_wallet_outbox_created_at_utc",
                table: "wallet_outbox",
                column: "created_at_utc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wallet_outbox");
        }
    }
}
