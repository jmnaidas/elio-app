using Elio.Domain.Catalog;
namespace Elio.Domain.Services;

public sealed class Service
{
    private Service() { }
    public Service(Guid organizationId, string name, string? description, decimal defaultUnitPrice, string currency)
    {
        if (organizationId == Guid.Empty) throw new ArgumentException("Organization is required.");
        Id = Guid.NewGuid(); OrganizationId = organizationId; CreatedAtUtc = DateTimeOffset.UtcNow;
        Update(name, description, defaultUnitPrice, currency);
    }
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = "";
    public string? Description { get; private set; }
    public decimal DefaultUnitPrice { get; private set; }
    public string Currency { get; private set; } = "";
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public Guid Version { get; private set; }
    public void Update(string name, string? description, decimal defaultUnitPrice, string currency)
    {
        var validName = CatalogRules.Required(name, 160, "Service name");
        var validDescription = CatalogRules.Optional(description, 2000, "Description");
        var validCurrency = CatalogRules.Currency(currency);
        if (defaultUnitPrice < 0 || defaultUnitPrice > 999999999999.99m || decimal.Round(defaultUnitPrice, 2) != defaultUnitPrice)
            throw new ArgumentException("Price must be between 0 and 999999999999.99, with at most two decimal places.");
        Name = validName; Description = validDescription; DefaultUnitPrice = defaultUnitPrice; Currency = validCurrency;
        Touch();
    }
    public void SetActive(bool active) { IsActive = active; Touch(); }
    private void Touch() { UpdatedAtUtc = DateTimeOffset.UtcNow; Version = Guid.NewGuid(); }
}
