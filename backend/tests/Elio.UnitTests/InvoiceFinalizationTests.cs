using Elio.Domain.Invoices;
namespace Elio.UnitTests;

public sealed class InvoiceFinalizationTests
{
    private static Invoice Draft() => new(Guid.NewGuid(), Guid.NewGuid(), "PHP", new(2026, 9, 29), new(2026, 10, 29), "Notes", "Bank transfer",
        [new(null, null, "Design", 1.5m, 10.01m)]);
    [Fact]
    public void Finalization_recalculates_and_captures_then_locks_all_editable_state()
    {
        var invoice = Draft(); var version = invoice.Version;
        typeof(InvoiceLine).GetProperty(nameof(InvoiceLine.LineTotal))!.SetValue(invoice.Lines.Single(), 999m);
        invoice.FinalizeInvoice(1, "Studio", "Asia/Manila", "Client", "billing@example.test", "+63 123", "Manila", false);
        Assert.Equal(15.02m, invoice.Total); Assert.Equal(15.02m, invoice.FinalizedSubtotal);
        Assert.Equal("INV-000001", invoice.InvoiceNumber); Assert.Equal("Studio", invoice.SellerName);
        Assert.Equal("Client", invoice.IssuedClientName); Assert.Equal("Manila", invoice.IssuedBillingAddress);
        Assert.False(invoice.IssuedClientIsActive); Assert.NotNull(invoice.FinalizedAtUtc); Assert.NotEqual(version, invoice.Version);
        Assert.Throws<ArgumentException>(() => invoice.Update(Guid.NewGuid(), "USD", new(2026, 1, 1), new(2026, 2, 1), "Changed", null, []));
        Assert.Throws<ArgumentException>(() => invoice.FinalizeInvoice(2, "Other", "UTC", "Other", "other@example.test", null, null, true));
        Assert.Equal("INV-000001", invoice.InvoiceNumber); Assert.Equal("Notes", invoice.Notes); Assert.Equal("PHP", invoice.Currency);
    }
    [Theory, InlineData(0L), InlineData(-1L)]
    public void Invalid_sequence_does_not_change_draft(long sequence)
    {
        var invoice = Draft(); var version = invoice.Version;
        Assert.Throws<ArgumentException>(() => invoice.FinalizeInvoice(sequence, "Studio", "UTC", "Client", "email@example.test", null, null, true));
        Assert.Equal(InvoiceLifecycle.Draft, invoice.Lifecycle); Assert.Null(invoice.InvoiceNumber); Assert.Equal(version, invoice.Version);
    }
    [Fact]
    public void Complete_validation_runs_again_and_numbers_expand_beyond_six_digits()
    {
        var invalid = Draft(); typeof(Invoice).GetProperty(nameof(Invoice.DueDate))!.SetValue(invalid, new DateOnly(2020, 1, 1));
        Assert.Throws<ArgumentException>(() => invalid.FinalizeInvoice(1, "Studio", "UTC", "Client", "email@example.test", null, null, true));
        Assert.Equal(InvoiceLifecycle.Draft, invalid.Lifecycle); Assert.Null(invalid.SequenceValue);
        var invoice = Draft(); invoice.FinalizeInvoice(1000000, "Studio", "UTC", "Client", "email@example.test", null, null, true);
        Assert.Equal("INV-1000000", invoice.InvoiceNumber);
    }
}
