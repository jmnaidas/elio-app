using Elio.Domain.Invoices;
namespace Elio.UnitTests;

public sealed class DueSummaryTests
{
    [Theory]
    [InlineData(8, 100, "NotDue", 0)]
    [InlineData(7, 100, "DueSoon", 0)]
    [InlineData(1, 100, "DueSoon", 0)]
    [InlineData(0, 100, "DueToday", 0)]
    [InlineData(-1, 100, "Overdue", 1)]
    [InlineData(-31, 60, "Overdue", 31)]
    [InlineData(-100, 0, "Paid", 0)]
    public void Calendar_boundaries_and_paid_exclusion(int offset, decimal balance, string state, int days)
    {
        var today = new DateOnly(2026, 10, 2);
        Assert.Equal(new DueSummary(state, days), DueSummary.Calculate(today.AddDays(offset), today, balance));
    }
    [Fact]
    public void Organization_timezone_controls_date_across_midnight_and_dst()
    {
        var instant = DateTimeOffset.Parse("2026-10-01T16:30:00Z");
        Assert.Equal(new DateOnly(2026, 10, 2), DueSummary.BusinessDate(instant, "Asia/Manila"));
        Assert.Equal(new DateOnly(2026, 10, 1), DueSummary.BusinessDate(instant, "America/New_York"));
        Assert.Equal(new DateOnly(2026, 3, 8), DueSummary.BusinessDate(DateTimeOffset.Parse("2026-03-08T07:30:00Z"), "America/New_York"));
        Assert.Equal(1, DueSummary.Calculate(new(2026, 3, 7), new(2026, 3, 8), 10).DaysOverdue);
    }
    [Fact]
    public void Reminder_captures_financial_context_and_completed_outcomes_cannot_change()
    {
        var invoice = new Invoice(Guid.NewGuid(), Guid.NewGuid(), "PHP", new(2026, 9, 1), new(2026, 9, 30), null, null, [new(null, null, "Work", 1, 100)]);
        Assert.Throws<ArgumentException>(() => new InvoiceReminder(invoice, "a@example.test", "test", 0));
        invoice.FinalizeInvoice(1, "Seller", "Asia/Manila", "Client", "a@example.test", null, null, true);
        var attempt = new InvoiceReminder(invoice, " a@example.test ", "test", 40);
        Assert.Equal(40, attempt.AmountPaid); Assert.Equal(60, attempt.BalanceDue); Assert.Equal("a@example.test", attempt.RecipientEmail);
        attempt.Succeed(); Assert.Throws<InvalidOperationException>(() => attempt.Fail("delivery_failed"));
        Assert.Throws<ArgumentException>(() => new InvoiceReminder(invoice, "a@example.test", "test", 100));
        Assert.Throws<ArgumentException>(() => new InvoiceReminder(invoice, "a@example.test\r\nBcc: b@example.test", "test", 0));
    }
}
