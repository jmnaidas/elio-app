namespace Elio.Domain.Organizations;

public enum MembershipRole { Owner = 1 }

public sealed class Membership
{
    private Membership() { }
    public Membership(Guid userId, Guid organizationId)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        OrganizationId = organizationId;
        Role = MembershipRole.Owner;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid OrganizationId { get; private set; }
    public MembershipRole Role { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
}
