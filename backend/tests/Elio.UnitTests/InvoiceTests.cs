using System.Globalization;
using Elio.Domain.Invoices;
namespace Elio.UnitTests;

public sealed class InvoiceTests
{
    private static readonly DateOnly Date = new(2026, 9, 23);
    private static Invoice Create(params DraftLine[] lines) => new(Guid.NewGuid(), Guid.NewGuid(), "PHP", Date, Date, null, null, lines);
    private static DraftLine Line(decimal quantity = 1, decimal price = 10) => new(null, null, " Work ", quantity, price);
    [Theory]
    [InlineData("1.5", "10.01", "15.02")]
    [InlineData("0.25", "0.02", "0.01")]
    [InlineData("0.0001", "1.00", "0.00")]
    [InlineData("1", "0", "0.00")]
    public void Totals_use_decimal_and_round_each_line_away_from_zero(string quantity, string price, string expected)
    {
        var invoice = Create(Line(decimal.Parse(quantity, CultureInfo.InvariantCulture), decimal.Parse(price, CultureInfo.InvariantCulture)));
        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), invoice.Total);
        Assert.Equal(invoice.Subtotal, invoice.Total);
        Assert.Equal("Work", invoice.Lines.Single().Description);
        Assert.Equal(InvoiceLifecycle.Draft, invoice.Lifecycle);
    }
    [Fact]
    public void Subtotal_sums_rounded_lines_instead_of_rounding_the_unrounded_sum()
    {
        var invoice = Create(Line(0.25m, 0.02m), Line(0.25m, 0.02m));
        Assert.Equal(0.02m, invoice.Total);
    }
    [Theory]
    [InlineData("0", "1")]
    [InlineData("-1", "1")]
    [InlineData("0.00001", "1")]
    [InlineData("1000000", "1")]
    [InlineData("1", "-1")]
    [InlineData("1", "1.001")]
    [InlineData("1", "1000000000000")]
    [InlineData("2", "999999999999.99")]
    public void Invalid_precision_ranges_and_line_overflow_are_rejected(string quantity, string price) =>
        Assert.Throws<ArgumentException>(() => Create(Line(decimal.Parse(quantity, CultureInfo.InvariantCulture), decimal.Parse(price, CultureInfo.InvariantCulture))));
    [Fact]
    public void Rejects_invalid_header_empty_lines_and_total_overflow_without_mutation()
    {
        var invoice = Create(Line()); var version = invoice.Version;
        void Reject(Guid client, string currency, DateOnly issue, DateOnly due, DraftLine[] lines) =>
            Assert.Throws<ArgumentException>(() => invoice.Update(client, currency, issue, due, null, null, lines));
        Reject(Guid.Empty, "PHP", Date, Date, [Line()]);
        Reject(invoice.ClientId, "EUR", Date, Date, [Line()]);
        Reject(invoice.ClientId, "PHP", default, Date, [Line()]);
        Reject(invoice.ClientId, "PHP", Date, Date.AddDays(-1), [Line()]);
        Reject(invoice.ClientId, "PHP", Date, Date, []);
        Reject(invoice.ClientId, "PHP", Date, Date, [Line() with { Description = " " }]);
        Reject(invoice.ClientId, "PHP", Date, Date, [Line(1, Invoice.MaxMoney), Line(1, 1)]);
        Assert.Equal(version, invoice.Version); Assert.Equal(10, invoice.Total); Assert.Single(invoice.Lines);
    }
    [Fact]
    public void Replace_lines_retains_ids_and_supports_removal_addition_and_ordering()
    {
        var invoice = Create(Line(), Line(2)); var original = invoice.Lines.ToArray(); var created = invoice.CreatedAtUtc;
        var version = invoice.Version;
        invoice.Update(invoice.ClientId, "USD", Date, Date.AddDays(7), " Note ", " Pay ",
            [new(original[1].Id, null, "Retained", 1.5m, 20), Line(3)]);
        var lines = invoice.Lines.OrderBy(x => x.SortOrder).ToArray();
        Assert.Equal(original[1].Id, lines[0].Id); Assert.DoesNotContain(invoice.Lines, x => x.Id == original[0].Id);
        Assert.Equal(new[] { 0, 1 }, lines.Select(x => x.SortOrder)); Assert.Equal(60, invoice.Total);
        Assert.Equal(created, invoice.CreatedAtUtc); Assert.NotEqual(version, invoice.Version);
        Assert.Equal("Note", invoice.Notes); Assert.Equal("Pay", invoice.PaymentInstructions);
    }
    [Fact]
    public void Foreign_or_duplicate_line_ids_are_rejected()
    {
        var invoice = Create(Line()); var id = invoice.Lines.Single().Id;
        foreach (var lines in new[] { new[] { Line() with { Id = Guid.NewGuid() } }, new[] { Line() with { Id = id }, Line() with { Id = id } } })
            Assert.Throws<ArgumentException>(() => invoice.Update(invoice.ClientId, "PHP", Date, Date, null, null, lines));
    }
}
