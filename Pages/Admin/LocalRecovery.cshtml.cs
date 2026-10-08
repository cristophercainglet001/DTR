using System.Security.Cryptography;
using DepEdDTRSystem.Data;
using DepEdDTRSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace DepEdDTRSystem.Pages.Admin;

[AllowAnonymous]
[EnableRateLimiting("auth")]
public sealed class LocalRecoveryModel : PageModel
{
    private const string RecoveryUsername = "admin";
    private const string Uppercase = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lowercase = "abcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Symbols = "!@#$%*-_";
    private static readonly string PasswordAlphabet = Uppercase + Lowercase + Digits + Symbols;

    private readonly DtrDbContext _db;
    private readonly IPasswordHasher<AdminAccount> _passwordHasher;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<LocalRecoveryModel> _logger;

    public LocalRecoveryModel(
        DtrDbContext db,
        IPasswordHasher<AdminAccount> passwordHasher,
        IWebHostEnvironment environment,
        ILogger<LocalRecoveryModel> logger)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _environment = environment;
        _logger = logger;
    }

    public string? TemporaryPassword { get; private set; }

    public string? ErrorMessage { get; private set; }

    public IActionResult OnGet()
    {
        Response.Headers.CacheControl = "no-store";
        return IsLocalDevelopmentRequest() ? Page() : NotFound();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (!IsLocalDevelopmentRequest())
        {
            return NotFound();
        }

        var adminMatches = await _db.AdminAccounts
            .Where(x => x.Username.ToUpper() == RecoveryUsername.ToUpper())
            .OrderBy(x => x.Id)
            .Take(2)
            .ToListAsync(cancellationToken);

        if (adminMatches.Count > 1)
        {
            ErrorMessage = "More than one admin account matches the recovery username. Resolve the duplicate accounts before continuing.";
            return Page();
        }

        var account = adminMatches.SingleOrDefault();
        if (account is null)
        {
            var accounts = await _db.AdminAccounts
                .OrderBy(x => x.Id)
                .Take(2)
                .ToListAsync(cancellationToken);

            if (accounts.Count != 1)
            {
                ErrorMessage = "Local recovery requires exactly one existing admin account.";
                return Page();
            }

            account = accounts[0];
            account.Username = RecoveryUsername;
        }

        var temporaryPassword = GenerateTemporaryPassword();
        var now = DateTime.UtcNow;
        account.PasswordHash = _passwordHasher.HashPassword(account, temporaryPassword);
        account.MustChangePassword = true;
        account.SecurityStamp = Guid.NewGuid().ToString("N");
        account.PasswordResetTokenHash = null;
        account.PasswordResetTokenExpiresAt = null;
        account.FailedLoginAttempts = 0;
        account.LockoutEnd = null;
        account.UpdatedAt = now;

        _db.AuditLogs.Add(new AuditLog
        {
            Action = "LocalPasswordRecovery",
            EntityName = "AdminAccount",
            EntityId = account.Id.ToString(),
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            CreatedAt = now,
        });

        await _db.SaveChangesAsync(cancellationToken);
        TemporaryPassword = temporaryPassword;
        return Page();
    }

    private bool IsLocalDevelopmentRequest()
    {
        if (!_environment.IsDevelopment())
        {
            _logger.LogWarning("Blocked admin local recovery outside Development.");
            return false;
        }

        var remoteIp = HttpContext.Connection.RemoteIpAddress;
        if (remoteIp is not null && System.Net.IPAddress.IsLoopback(remoteIp))
        {
            return true;
        }

        _logger.LogWarning(
            "Blocked admin local recovery from non-loopback address {RemoteIp}.",
            remoteIp);
        return false;
    }

    private static string GenerateTemporaryPassword()
    {
        var characters = new char[24];
        characters[0] = Pick(Uppercase);
        characters[1] = Pick(Lowercase);
        characters[2] = Pick(Digits);
        characters[3] = Pick(Symbols);

        for (var index = 4; index < characters.Length; index++)
        {
            characters[index] = Pick(PasswordAlphabet);
        }

        for (var index = characters.Length - 1; index > 0; index--)
        {
            var swapIndex = RandomNumberGenerator.GetInt32(index + 1);
            (characters[index], characters[swapIndex]) = (characters[swapIndex], characters[index]);
        }

        return new string(characters);
    }

    private static char Pick(string alphabet) =>
        alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
}
