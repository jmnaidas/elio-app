using Elio.Domain.Invoices;
namespace Elio.UnitTests;
public sealed class InvoicePaymentTests
{
    private static Invoice Issued(decimal total = 100)
    {
        var invoice = new Invoice(Guid.NewGuid(), Guid.NewGuid(), "PHP", new(2026, 9, 1), new(2026, 9, 30), null, null, [new(null, null, "Work", 1, total)]);
        invoice.FinalizeInvoice(1, "Seller", "Asia/Manila", "Client", "client@example.test", null, null, true); return invoice;
    }
    [Theory, InlineData("0"), InlineData("-1"), InlineData("0.001"), InlineData("1000000000000")]
    public void Invalid_money_is_rejected_without_rounding(string amount) =>
        Assert.Throws<ArgumentException>(() => new InvoicePayment(Issued(), decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), DateTimeOffset.UtcNow, "Cash", null, null, Guid.NewGuid()));
    [Theory, InlineData(100, 0, 100, "Unpaid"), InlineData(100, 40, 60, "PartiallyPaid"), InlineData(100, 100, 0, "Paid"), InlineData(0, 0, 0, "Paid")]
    public void Summary_is_derived_and_zero_value_invoices_have_no_balance(decimal total, decimal paid, decimal due, string state)
    { var result = PaymentSummary.Calculate(total, paid); Assert.Equal(due, result.BalanceDue); Assert.Equal(state, result.PaymentStatus); Assert.Equal(paid, result.AmountPaid); }
    [Fact]
    public void Payment_copies_currency_normalizes_utc_and_trims_text_without_mutating_invoice()
    {
        var invoice = Issued(); var version = invoice.Version; var time = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.FromHours(8));
        var payment = new InvoicePayment(invoice, 0.01m, time, "BankTransfer", "  REF  ", " note ", Guid.NewGuid());
        Assert.Equal("PHP", payment.Currency); Assert.Equal(TimeSpan.Zero, payment.ReceivedAtUtc.Offset); Assert.Equal(time, payment.ReceivedAtUtc);
        Assert.Equal("REF", payment.Reference); Assert.Equal("note", payment.Notes); Assert.Equal(version, invoice.Version); Assert.Equal(100, invoice.Total);
        Assert.Throws<ArgumentException>(() => PaymentSummary.Calculate(100, 101));
        Assert.Throws<ArgumentException>(() => new InvoicePayment(invoice, 1, default, "Cash", null, null, Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => new InvoicePayment(invoice, 1, DateTimeOffset.UtcNow.AddDays(1), "Cash", null, null, Guid.NewGuid()));
        foreach (var method in new[] { "0", "Unknown", "cash", "Cash, Card" })
            Assert.Throws<ArgumentException>(() => new InvoicePayment(invoice, 1, time, method, null, null, Guid.NewGuid()));
    }
}
