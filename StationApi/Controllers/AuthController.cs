using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using StationApi.DataDbContext;
using StationApi.Models;

namespace StationApi.Controllers;
public class RegisterRequest
{
    [Required, StringLength(100, MinimumLength = 2)] public string Name { get; set; } = "";
    [Required, EmailAddress, StringLength(160)] public string Email { get; set; } = "";
    [Required, StringLength(128, MinimumLength = 10)] public string Password { get; set; } = "";
    public RegisterRequest() { }
    public RegisterRequest(string name, string email, string password) { Name = name; Email = email; Password = password; }
}
public record LoginRequest([Required, EmailAddress] string Email, [Required] string Password);
public record UserView(int Id, string Name, string Email, string Role, bool Enabled, DateTime CreatedAt)
{
    public static UserView From(FleetUser user) => new(user.Id, user.Name, user.Email, user.Role, user.Enabled, user.CreatedAt);
}
[ApiController, Route("api/auth")]
public class AuthController(OperationsDbContext db, IPasswordHasher<FleetUser> passwords) : ControllerBase
{
    [HttpGet("csrf"), AllowAnonymous]
    public IActionResult Csrf([FromServices] IAntiforgery antiforgery) => Ok(new { token = antiforgery.GetAndStoreTokens(HttpContext).RequestToken });
    [HttpPost("login"), AllowAnonymous, EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(x => x.Email == email);
        if (user is null || !user.Enabled || passwords.VerifyHashedPassword(user, user.PasswordHash, request.Password) == PasswordVerificationResult.Failed)
            return Unauthorized(new { message = "Email or password is incorrect, or the account is disabled." });
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Name, user.Name),
            new Claim(ClaimTypes.Role, user.Role), new Claim("version", user.SessionVersion.ToString()) };
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
        return Ok(UserView.From(user));
    }
    [HttpPost("register"), AllowAnonymous, EnableRateLimiting("login")]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(x => x.Email == email)) return Conflict(new { message = "This email is already registered." });
        var user = new FleetUser { Name = request.Name.Trim(), Email = email, Role = "RIDER" };
        user.PasswordHash = passwords.HashPassword(user, request.Password);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return Ok(new { message = "Account created. You can sign in now." });
    }
    [HttpGet("me"), Authorize]
    public async Task<IActionResult> Me() => Ok(UserView.From((await db.Users.FindAsync(int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!)))!));
    [HttpPost("logout"), Authorize]
    public async Task<IActionResult> Logout() { await HttpContext.SignOutAsync(); return NoContent(); }
}
