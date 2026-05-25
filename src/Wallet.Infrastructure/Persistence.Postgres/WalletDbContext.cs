using Microsoft.EntityFrameworkCore;

namespace Wallet.Infrastructure.Persistence.Postgres;

public sealed class WalletDbContext(DbContextOptions<WalletDbContext> options) : DbContext(options)
{
    public DbSet<WalletStateEntity> Wallets => Set<WalletStateEntity>();
    public DbSet<WalletOutboxEntity> OutboxEvents => Set<WalletOutboxEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WalletStateEntity>(e =>
        {
            e.ToTable("wallets");
            e.HasKey(w => w.PlayerId);
            e.HasIndex(w => new { w.PlayerId, w.Version }).HasDatabaseName("ix_wallets_player_id_version");
            e.Property(w => w.PlayerId).HasColumnName("player_id").IsRequired();
            e.Property(w => w.Balance).HasColumnName("balance").HasColumnType("numeric(18,4)").IsRequired();
            e.Property(w => w.Version).HasColumnName("version").IsRequired().IsConcurrencyToken();
            e.Property(w => w.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
            e.Property(w => w.WalletType).HasColumnName("wallet_type").HasConversion<string>().IsRequired();
            e.Property(w => w.CurrencyType).HasColumnName("currency_type").HasConversion<string>().IsRequired();
            e.Property(w => w.CreatedAt).HasColumnName("created_at").IsRequired();
            e.Property(w => w.ExpiresAt).HasColumnName("expires_at");
        });

        modelBuilder.Entity<WalletOutboxEntity>(e =>
        {
            e.ToTable("wallet_outbox");
            e.HasKey(x => x.EventId);
            e.Property(x => x.EventId).HasColumnName("event_id").IsRequired();
            e.Property(x => x.PlayerId).HasColumnName("player_id").IsRequired();
            e.Property(x => x.EventType).HasColumnName("event_type").IsRequired();
            e.Property(x => x.Amount).HasColumnName("amount").HasColumnType("numeric(18,4)").IsRequired();
            e.Property(x => x.BalanceAfter).HasColumnName("balance_after").HasColumnType("numeric(18,4)").IsRequired();
            e.Property(x => x.OccurredAtUtc).HasColumnName("occurred_at_utc").IsRequired();
            e.Property(x => x.Version).HasColumnName("version").IsRequired();
            e.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
            e.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
            e.HasIndex(x => x.CreatedAtUtc).HasDatabaseName("ix_wallet_outbox_created_at_utc");
        });
    }
}
