using System.ComponentModel.DataAnnotations;
using Elio.Domain.Catalog;
namespace Elio.Domain.Clients;

public sealed class Client
{
    private Client() { }
    public Client(Guid organizationId, string name, string email, string? phone, string? billingAddress, string? notes, string currency)
    {
        if (organizationId == Guid.Empty) throw new ArgumentException("Organization is required.");
        Id = Guid.NewGuid(); OrganizationId = organizationId; CreatedAtUtc = DateTimeOffset.UtcNow;
        Update(name, email, phone, billingAddress, notes, currency);
    }
    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = "";
    public string Email { get; private set; } = "";
    public string? Phone { get; private set; }
    public string? BillingAddress { get; private set; }
    public string? Notes { get; private set; }
    public string Currency { get; private set; } = "";
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public Guid Version { get; private set; }
    public void Update(string name, string email, string? phone, string? billingAddress, string? notes, string currency)
    {
        var validName = CatalogRules.Required(name, 160, "Client name");
        var validEmail = CatalogRules.Required(email, 254, "Email");
        if (!new EmailAddressAttribute().IsValid(validEmail)) throw new ArgumentException("Enter a valid email address.");
        var validPhone = CatalogRules.Optional(phone, 50, "Phone");
        var validAddress = CatalogRules.Optional(billingAddress, 1000, "Billing address");
        var validNotes = CatalogRules.Optional(notes, 2000, "Notes");
        var validCurrency = CatalogRules.Currency(currency);
        Name = validName; Email = validEmail; Phone = validPhone; BillingAddress = validAddress; Notes = validNotes; Currency = validCurrency;
        Touch();
    }
    public void SetActive(bool active) { IsActive = active; Touch(); }
    private void Touch() { UpdatedAtUtc = DateTimeOffset.UtcNow; Version = Guid.NewGuid(); }
}
