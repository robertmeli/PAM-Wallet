using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wallet.Infrastructure.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddWalletTypeAndCurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "created_at",
                table: "wallets",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<string>(
                name: "currency_type",
                table: "wallets",
                type: "text",
                nullable: false,
                defaultValue: "EUR");

            migrationBuilder.AddColumn<DateTime>(
                name: "expires_at",
                table: "wallets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "wallet_type",
                table: "wallets",
                type: "text",
                nullable: false,
                defaultValue: "Main");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "created_at",
                table: "wallets");

            migrationBuilder.DropColumn(
                name: "currency_type",
                table: "wallets");

            migrationBuilder.DropColumn(
                name: "expires_at",
                table: "wallets");

            migrationBuilder.DropColumn(
                name: "wallet_type",
                table: "wallets");
        }
    }
}
