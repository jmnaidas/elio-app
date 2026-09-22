using System.Text;
using Elio.Application.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Elio.Infrastructure.Identity;

public sealed class AccountService(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
    IAccountEmailSender email, IConfiguration configuration, IHostEnvironment environment) : IAccountService
{
    public async Task RegisterAsync(RegisterRequest request)
    {
        email.EnsureAvailable();
        var user = new ApplicationUser { Id = Guid.NewGuid(), Email = request.Email.Trim(), UserName = request.Email.Trim() };
        // Validate password independently of account existence to avoid an enumeration signal.
        foreach (var validator in users.PasswordValidators)
            Ensure(await validator.ValidateAsync(users, user, request.Password));
        if (await users.FindByEmailAsync(user.Email) is not null) return;
        IdentityResult result;
        try { result = await users.CreateAsync(user, request.Password); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }) { return; }
        if (result.Errors.Any(x => x.Code is "DuplicateEmail" or "DuplicateUserName")) return;
        Ensure(result);
        await SendVerification(user);
    }
    public async Task LoginAsync(LoginRequest request)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null) throw new RequestFailure(401, "Email or password is incorrect, or sign-in is temporarily unavailable.");
        var result = await signIn.PasswordSignInAsync(user, request.Password, false, lockoutOnFailure: true);
        if (!result.Succeeded) throw new RequestFailure(401, "Email or password is incorrect, or sign-in is temporarily unavailable.");
    }
    public async Task LogoutAsync(Guid userId)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is not null) Ensure(await users.UpdateSecurityStampAsync(user));
        await signIn.SignOutAsync();
    }
    public async Task<UserDto?> GetUserAsync(Guid userId)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        return user is null ? null : new(user.Id, user.Email!, user.EmailConfirmed);
    }
    public async Task VerifyAsync(VerifyRequest request)
    {
        if (!Guid.TryParse(request.UserId, out _)) throw InvalidToken();
        var user = await users.FindByIdAsync(request.UserId);
        if (user is null || !Guid.TryParse(request.UserId, out _)) throw InvalidToken();
        var result = await users.ConfirmEmailAsync(user, Decode(request.Token));
        if (!result.Succeeded) throw InvalidToken();
    }
    public async Task ResendAsync(string address)
    {
        email.EnsureAvailable();
        var user = await users.FindByEmailAsync(address.Trim());
        if (user is { EmailConfirmed: false }) await SendVerification(user);
    }
    public async Task ForgotAsync(string address)
    {
        email.EnsureAvailable();
        var user = await users.FindByEmailAsync(address.Trim());
        if (user is not { EmailConfirmed: true }) return;
        await SendLink(user, "reset-password", await users.GeneratePasswordResetTokenAsync(user));
    }
    public async Task ResetAsync(ResetRequest request)
    {
        if (!Guid.TryParse(request.UserId, out _)) throw InvalidToken();
        var user = await users.FindByIdAsync(request.UserId);
        if (user is null) throw InvalidToken();
        var result = await users.ResetPasswordAsync(user, Decode(request.Token), request.Password);
        if (!result.Succeeded) throw new RequestFailure(400, "The reset link is invalid or expired, or the password does not meet the requirements.");
        // ResetPassword updates the security stamp, invalidating existing cookies on their next request.
    }
    private Task SendVerification(ApplicationUser user) => SendVerificationCore(user);
    private async Task SendVerificationCore(ApplicationUser user) =>
        await SendLink(user, "verify-email", await users.GenerateEmailConfirmationTokenAsync(user));
    private Task SendLink(ApplicationUser user, string purpose, string token)
    {
        var origin = configuration["AccountEmail:FrontendOrigin"] ?? (environment.IsDevelopment() ? "http://localhost:4200" : "");
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || (!environment.IsDevelopment() && uri.Scheme != "https")
            || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new RequestFailure(503, "Account email delivery is not configured.");
        var encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        // Fragment tokens never reach access logs or the HTTP Referer header.
        return email.SendAsync(user.Email!, purpose, $"{origin.TrimEnd('/')}/{purpose}#userId={user.Id}&token={encoded}");
    }
    private static string Decode(string token)
    {
        try { return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token)); }
        catch (FormatException) { throw InvalidToken(); }
    }
    private static RequestFailure InvalidToken() => new(400, "This link is invalid or expired. Request a new link.");
    private static void Ensure(IdentityResult result)
    {
        if (!result.Succeeded) throw new RequestFailure(400, "Please check the account details.",
            new Dictionary<string, string[]> { ["account"] = result.Errors.Select(x => x.Description).ToArray() });
    }
}
