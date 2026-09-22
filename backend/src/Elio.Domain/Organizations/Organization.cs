namespace Elio.Domain.Organizations;

public sealed class Organization
{
    private Organization() { }
    public Organization(string name, string timeZone, string currency)
    {
        Id = Guid.NewGuid();
        CreatedAtUtc = DateTimeOffset.UtcNow;
        Update(name, timeZone, currency);
    }
    public Guid Id { get; private set; }
    public string Name { get; private set; } = "";
    public string TimeZone { get; private set; } = "";
    public string DefaultCurrency { get; private set; } = "";
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public Guid Version { get; private set; }
    public void Update(string name, string timeZone, string currency)
    {
        name = name.Trim();
        if (name.Length is < 2 or > 120) throw new ArgumentException("Organization name must contain 2–120 characters.");
        if (currency is not ("PHP" or "USD")) throw new ArgumentException("Choose PHP or USD.");
        if (timeZone.Length > 100 || !TimeZoneInfo.TryConvertIanaIdToWindowsId(timeZone, out _))
            throw new ArgumentException("Choose a valid IANA timezone, such as Asia/Manila.");
        Name = name;
        TimeZone = timeZone;
        DefaultCurrency = currency;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
        Version = Guid.NewGuid();
    }
}
