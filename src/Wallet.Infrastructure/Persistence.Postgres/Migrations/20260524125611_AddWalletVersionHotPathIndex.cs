using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wallet.Infrastructure.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddWalletVersionHotPathIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_wallets_player_id_version",
                table: "wallets",
                columns: new[] { "player_id", "version" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_wallets_player_id_version",
                table: "wallets");
        }
    }
}
