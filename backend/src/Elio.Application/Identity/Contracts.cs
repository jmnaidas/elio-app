using System.ComponentModel.DataAnnotations;
using Elio.Domain.Organizations;

namespace Elio.Application.Identity;

public sealed record RegisterRequest(
    [Required, EmailAddress, MaxLength(254)] string Email,
    [Required, MinLength(12), MaxLength(128)] string Password,
    [Required] string ConfirmPassword) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (Password != ConfirmPassword) yield return new("Passwords must match.", [nameof(ConfirmPassword)]);
    }
}
public sealed record LoginRequest([Required, EmailAddress, MaxLength(254)] string Email, [Required, MaxLength(128)] string Password);
public sealed record EmailRequest([Required, EmailAddress, MaxLength(254)] string Email);
public sealed record VerifyRequest([Required] string UserId, [Required, MaxLength(4096)] string Token);
public sealed record ResetRequest([Required] string UserId, [Required, MaxLength(4096)] string Token,
    [Required, MinLength(12), MaxLength(128)] string Password, [Required] string ConfirmPassword) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (Password != ConfirmPassword) yield return new("Passwords must match.", [nameof(ConfirmPassword)]);
    }
}
public sealed record OrganizationRequest([Required, MaxLength(120)] string Name, [Required, MaxLength(100)] string TimeZone,
    [Required] string DefaultCurrency, Guid? Version = null);
public sealed record OrganizationDto(Guid Id, string Name, string TimeZone, string DefaultCurrency, Guid Version);
public sealed record UserDto(Guid Id, string Email, bool EmailVerified);
public sealed record MembershipDto(Guid Id, Guid OrganizationId, string Role);
public sealed record SessionDto(UserDto? User, OrganizationDto? Organization, MembershipDto? Membership);
public sealed record AccessContext(UserDto User, MembershipDto? Membership);

public sealed class RequestFailure(int status, string message, Dictionary<string, string[]>? errors = null) : Exception(message)
{
    public int Status { get; } = status;
    public Dictionary<string, string[]>? Errors { get; } = errors;
}

public interface IAccountService
{
    Task RegisterAsync(RegisterRequest request);
    Task LoginAsync(LoginRequest request);
    Task LogoutAsync(Guid userId);
    Task<UserDto?> GetUserAsync(Guid userId);
    Task VerifyAsync(VerifyRequest request);
    Task ResendAsync(string email);
    Task ForgotAsync(string email);
    Task ResetAsync(ResetRequest request);
}
public interface IAccountEmailSender
{
    void EnsureAvailable();
    Task SendAsync(string recipient, string purpose, string link);
}
public interface IOrganizationService
{
    Task<MembershipDto?> ResolveMembershipAsync(Guid userId, Guid? requestedOrganization);
    Task<OrganizationDto> GetAsync(Guid userId, Guid organizationId);
    Task<OrganizationDto> CreateAsync(Guid userId, OrganizationRequest request);
    Task<OrganizationDto> UpdateAsync(Guid userId, Guid organizationId, OrganizationRequest request);
}
public interface ICurrentOrganization
{
    Task<AccessContext?> GetAsync();
}
