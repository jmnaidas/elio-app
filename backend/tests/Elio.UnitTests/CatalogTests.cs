using Elio.Domain.Clients;
using Elio.Domain.Services;
namespace Elio.UnitTests;

public sealed class CatalogTests
{
    [Theory]
    [InlineData("", "email@example.test", "PHP")]
    [InlineData("Client", "invalid", "PHP")]
    [InlineData("Client", "email@example.test", "EUR")]
    public void Invalid_client_details_are_rejected(string name, string email, string currency) =>
        Assert.Throws<ArgumentException>(() => new Client(Guid.NewGuid(), name, email, null, null, null, currency));
    [Theory, InlineData("-1"), InlineData("1.001"), InlineData("1000000000000")]
    public void Invalid_prices_are_rejected_without_rounding(string price) =>
        Assert.Throws<ArgumentException>(() => new Service(Guid.NewGuid(), "Consulting", null, decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture), "PHP"));
    [Theory, InlineData("PHP"), InlineData("USD")]
    public void Zero_price_and_inactive_history_are_supported(string currency)
    {
        var item = new Service(Guid.NewGuid(), " Consulting ", " Description ", 0m, currency);
        var created = item.CreatedAtUtc; var id = item.Id; var version = item.Version;
        item.SetActive(false); Assert.False(item.IsActive); Assert.Equal(0m, item.DefaultUnitPrice);
        Assert.NotEqual(version, item.Version); item.SetActive(true); Assert.True(item.IsActive);
        Assert.Equal(created, item.CreatedAtUtc); Assert.Equal(id, item.Id); Assert.Equal("Consulting", item.Name);
    }
    [Fact]
    public void Client_updates_are_validated_before_mutation_and_preserve_tenant_and_creation()
    {
        var org = Guid.NewGuid(); var client = new Client(org, " Client ", " hello@example.test ", " ", null, " Notes ", "PHP");
        var created = client.CreatedAtUtc;
        Assert.Null(client.Phone); Assert.Equal("Notes", client.Notes);
        Assert.Throws<ArgumentException>(() => client.Update("Changed", "invalid", null, null, null, "PHP"));
        Assert.Equal("Client", client.Name);
        client.Update("Updated", "other@example.test", null, "Address", null, "USD"); client.SetActive(false); client.SetActive(true);
        Assert.Equal(org, client.OrganizationId); Assert.Equal(created, client.CreatedAtUtc); Assert.Equal("USD", client.Currency);
    }
}
