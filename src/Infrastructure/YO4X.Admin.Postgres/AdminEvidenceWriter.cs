using YO4X.Audit;
using YO4X.Outbox;
using YO4X.Persistence.Postgres;

namespace YO4X.Admin.Postgres;

internal static class AdminEvidenceWriter
{
    public static async Task AppendCommandEventAsync(
        TenantPostgresTransaction transaction,
        Guid commandId,
        string action,
        string messageType,
        string reasonCode,
        object redactedPayload,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        AuditEvent audit = AuditEvent.Create(
            transaction.Context.TenantId,
            transaction.Context.ActorId,
            AuditCategory.Operations,
            action,
            "admin_command",
            commandId.ToString("D"),
            AuditOutcome.Accepted,
            reasonCode,
            transaction.Context.CorrelationId,
            causationId: null,
            redactedPayload,
            occurredAt);
        OutboxMessage outbox = OutboxMessage.Create(
            transaction.Context.TenantId,
            messageType,
            "admin_command",
            commandId.ToString("D"),
            redactedPayload,
            transaction.Context.CorrelationId,
            causationId: null,
            occurredAt);
        await PostgresAuditOutboxWriter.AppendAsync(
            transaction,
            audit,
            outbox,
            cancellationToken).ConfigureAwait(false);
    }
}
