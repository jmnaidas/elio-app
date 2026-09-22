using System.Security.Claims;
using Elio.Application.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Elio.Api.Identity;

[ApiController, Route("api/auth")]
public sealed class AuthController(IAccountService accounts, ICurrentOrganization current,
    IOrganizationService organizations, IAntiforgery antiforgery, IHostEnvironment environment) : ControllerBase
{
    private static object EmailResponse => new { message = "If the account is eligible, an email with the next steps will arrive shortly." };

    [HttpGet("antiforgery")]
    public IActionResult Antiforgery()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!, new CookieOptions
        { HttpOnly = false, Secure = !environment.IsDevelopment() || Request.IsHttps, SameSite = SameSiteMode.Lax, Path = "/" });
        return Ok(new { requestToken = tokens.RequestToken });
    }
    [HttpGet("session")]
    public async Task<SessionDto> Session()
    {
        var access = await current.GetAsync();
        if (access is null) return new(null, null, null);
        var organization = access.User.EmailVerified && access.Membership is not null
            ? await organizations.GetAsync(access.User.Id, access.Membership.OrganizationId) : null;
        return new(access.User, organization, access.Membership);
    }
    [HttpPost("register"), EnableRateLimiting("account")]
    public async Task<IActionResult> Register(RegisterRequest request) { await accounts.RegisterAsync(request); return Ok(EmailResponse); }
    [HttpPost("login"), EnableRateLimiting("account")]
    public async Task<IActionResult> Login(LoginRequest request) { await accounts.LoginAsync(request); return NoContent(); }
    [HttpPost("logout"), Authorize]
    public async Task<IActionResult> Logout() { await accounts.LogoutAsync(Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!)); return NoContent(); }
    [HttpPost("verify-email"), EnableRateLimiting("account")]
    public async Task<IActionResult> Verify(VerifyRequest request) { await accounts.VerifyAsync(request); return NoContent(); }
    [HttpPost("resend-verification"), EnableRateLimiting("account")]
    public async Task<IActionResult> Resend(EmailRequest request) { await accounts.ResendAsync(request.Email); return Ok(EmailResponse); }
    [HttpPost("forgot-password"), EnableRateLimiting("account")]
    public async Task<IActionResult> Forgot(EmailRequest request) { await accounts.ForgotAsync(request.Email); return Ok(EmailResponse); }
    [HttpPost("reset-password"), EnableRateLimiting("account")]
    public async Task<IActionResult> Reset(ResetRequest request) { await accounts.ResetAsync(request); return NoContent(); }
}
