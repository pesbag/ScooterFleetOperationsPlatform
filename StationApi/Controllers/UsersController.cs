using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StationApi.DataDbContext;
using StationApi.Models;

namespace StationApi.Controllers;
public record UserRequest([Required, StringLength(100, MinimumLength = 2)] string Name,
    [Required, EmailAddress, StringLength(160)] string Email,
    [Required, RegularExpression("^(ADMIN|OPERATOR)$")] string Role, bool Enabled,
    [StringLength(128, MinimumLength = 10)] string? Password);
public record EnabledRequest(bool Enabled);
[ApiController, Authorize(Roles = "ADMIN"), Route("api/users")]
public class UsersController(OperationsDbContext db, IPasswordHasher<FleetUser> passwords) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> All() => Ok((await db.Users.AsNoTracking().OrderBy(x => x.Name).ToListAsync()).Select(UserView.From));
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id) => await db.Users.FindAsync(id) is { } user ? Ok(UserView.From(user)) : NotFound();
    [HttpPost]
    public async Task<IActionResult> Create(UserRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 10)
            return BadRequest(new { message = "A password of at least 10 characters is required." });
        var email = request.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(x => x.Email == email)) return Conflict(new { message = "This email is already registered." });
        var user = new FleetUser { Name = request.Name.Trim(), Email = email, Role = request.Role, Enabled = request.Enabled };
        user.PasswordHash = passwords.HashPassword(user, request.Password);
        db.Users.Add(user); await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = user.Id }, UserView.From(user));
    }
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, UserRequest request)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var user = await db.Users.FindAsync(id);
        if (user is null) return NotFound();
        if (id == int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!) && (!request.Enabled || request.Role != "ADMIN"))
            return BadRequest(new { message = "You cannot disable or demote your own account." });
        if (user.Role == "ADMIN" && user.Enabled && (request.Role != "ADMIN" || !request.Enabled)
            && await db.Users.CountAsync(x => x.Role == "ADMIN" && x.Enabled) <= 1)
            return Conflict(new { message = "At least one enabled administrator is required." });
        var email = request.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(x => x.Email == email && x.Id != id)) return Conflict(new { message = "This email is already registered." });
        user.Name = request.Name.Trim(); user.Email = email; user.Role = request.Role; user.Enabled = request.Enabled; user.SessionVersion++;
        if (!string.IsNullOrEmpty(request.Password)) user.PasswordHash = passwords.HashPassword(user, request.Password);
        await db.SaveChangesAsync(); await transaction.CommitAsync();
        return Ok(UserView.From(user));
    }
    [HttpPatch("{id:int}/enabled")]
    public async Task<IActionResult> Enable(int id, EnabledRequest request)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        return user is null ? NotFound() : await Update(id, new(user.Name, user.Email, user.Role, request.Enabled, null));
    }
    // Disable instead of deleting: keep operational audit references valid.
    [HttpDelete("{id:int}")]
    public Task<IActionResult> Delete(int id) => Enable(id, new(false));
}
