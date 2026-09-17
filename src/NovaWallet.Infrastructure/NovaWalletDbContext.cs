using Microsoft.EntityFrameworkCore;
using NovaWallet.Domain.Entities;

namespace NovaWallet.Infrastructure;

public class NovaWalletDbContext : DbContext
{
    public NovaWalletDbContext(DbContextOptions<NovaWalletDbContext> options) : base(options) { }

    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<LedgerTransaction> LedgerTransactions => Set<LedgerTransaction>();
    public DbSet<AuditLogEntry> AuditLogs => Set<AuditLogEntry>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Wallet>(e =>
        {
            e.ToTable("wallets");
            e.HasKey(x => x.Id);
            e.Property(x => x.CustomerId).IsRequired().HasMaxLength(64);
            e.HasIndex(x => x.CustomerId);
            e.Property(x => x.Currency).HasMaxLength(3).HasDefaultValue("NGN");
            e.Property(x => x.BalanceKobo).IsRequired();
        });

        modelBuilder.Entity<LedgerTransaction>(e =>
        {
            e.ToTable("ledger_transactions");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.WalletId, x.CreatedAtUtc });
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            e.HasOne<Wallet>().WithMany().HasForeignKey(x => x.WalletId);
        });

        modelBuilder.Entity<AuditLogEntry>(e =>
        {
            e.ToTable("audit_logs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.HasIndex(x => new { x.WalletId, x.TimestampUtc });
        });

        modelBuilder.Entity<IdempotencyRecord>(e =>
        {
            e.ToTable("idempotency_records");
            e.HasKey(x => x.Key);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        });
    }
}
