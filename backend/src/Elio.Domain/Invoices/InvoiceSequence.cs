namespace Elio.Domain.Invoices;

// Allocated with a PostgreSQL upsert inside the same transaction as finalization.
public sealed class InvoiceSequence
{
    public Guid OrganizationId { get; private set; }
    public long LastValue { get; private set; }
}
