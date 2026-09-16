using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NovaWallet.Domain.Entities;

namespace NovaWallet.Infrastructure;

/// <summary>
/// Defence-in-depth for the "append-only audit trail" requirement: even if application
/// code somewhere accidentally tracks an AuditLogEntry for update/delete, SaveChanges
/// throws instead of silently mutating history. (In a production system this would be
/// backed up by a DB-level trigger or REVOKE UPDATE/DELETE grant as well.)
/// </summary>
public class AppendOnlyAuditInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Guard(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Guard(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void Guard(DbContext? context)
    {
        if (context is null) return;

        var mutatedAuditRows = context.ChangeTracker.Entries<AuditLogEntry>()
            .Any(e => e.State is EntityState.Modified or EntityState.Deleted);

        if (mutatedAuditRows)
            throw new InvalidOperationException("Audit log entries are append-only and cannot be modified or deleted.");
    }
}
