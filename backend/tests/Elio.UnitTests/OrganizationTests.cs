using Elio.Domain.Organizations;
namespace Elio.UnitTests;
public sealed class OrganizationTests
{
    [Theory]
    [InlineData(" ", "Asia/Manila", "PHP")]
    [InlineData("Studio", "+08:00", "PHP")]
    [InlineData("Studio", "Not/AZone", "PHP")]
    [InlineData("Studio", "Asia/Manila", "EUR")]
    public void Rejects_invalid_organization_settings(string name, string timeZone, string currency) => Assert.Throws<ArgumentException>(() => new Organization(name, timeZone, currency));
    [Fact]
    public void Stores_IANA_zone_trims_name_and_versions_changes()
    {
        var organization = new Organization("  Example Studio  ", "Asia/Manila", "PHP");
        Assert.Equal("Example Studio", organization.Name);
        var version = organization.Version;
        organization.Update("New name", "America/New_York", "USD");
        Assert.NotEqual(version, organization.Version);
        Assert.True(organization.UpdatedAtUtc >= organization.CreatedAtUtc);
    }
}
