using System.ComponentModel.DataAnnotations;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using DepEdDTRSystem.Data;
using DepEdDTRSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace DepEdDTRSystem.Pages.Admin;

[AllowAnonymous]
[EnableRateLimiting("auth")]
public class ForgotPasswordModel : PageModel
{
    private static readonly TimeSpan ResetTokenLifetime = TimeSpan.FromMinutes(20);

    private readonly DtrDbContext _db;
    private readonly IEmailSender _emailSender;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<ForgotPasswordModel> _logger;

    public ForgotPasswordModel(
        DtrDbContext db,
        IEmailSender emailSender,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        ILogger<ForgotPasswordModel> logger)
    {
        _db = db;
        _emailSender = emailSender;
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
    }

    [BindProperty]
    [Required]
    [EmailAddress]
    [StringLength(254)]
    public string Email { get; set; } = string.Empty;

    public bool RequestSubmitted { get; private set; }

    public IActionResult OnGet()
    {
        Response.Headers.CacheControl = "no-store";
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToPage("/Admin/Dashboard");
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var normalizedEmail = Email.Trim().ToUpper();
        var account = await _db.AdminAccounts
            .FirstOrDefaultAsync(x => x.Email.ToUpper() == normalizedEmail, cancellationToken);

        if (account is not null)
        {
            var publicBaseUrl = _configuration["App:PublicBaseUrl"]?.TrimEnd('/');
            if (!Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out var baseUri)
                || (baseUri.Scheme != Uri.UriSchemeHttps
                    && !(_environment.IsDevelopment() && baseUri.Scheme == Uri.UriSchemeHttp)))
            {
                _logger.LogError("Password reset was requested but App:PublicBaseUrl is not configured with a valid absolute URL.");
                RequestSubmitted = true;
                return Page();
            }

            var token = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(
                RandomNumberGenerator.GetBytes(32));
            account.PasswordResetTokenHash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(token)));
            account.PasswordResetTokenExpiresAt = DateTime.UtcNow.Add(ResetTokenLifetime);
            account.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

            var resetUrl = Url.Page(
                "/Admin/ResetPassword",
                pageHandler: null,
                values: new { token },
                protocol: baseUri.Scheme,
                host: baseUri.Authority,
                fragment: null);

            try
            {
                await _emailSender.SendAsync(
                    account.Email,
                    "Reset your DepEd DTR admin password",
                    $"A password reset was requested for your admin account. This link expires in 20 minutes and can only be used once:\n\n{resetUrl}\n\nIf you did not request this, you can ignore this email.",
                    cancellationToken);
            }
            catch (Exception ex) when (ex is InvalidOperationException
                                       or SmtpException
                                       or IOException
                                       or FormatException
                                       or ArgumentException)
            {
                account.PasswordResetTokenHash = null;
                account.PasswordResetTokenExpiresAt = null;
                await _db.SaveChangesAsync(CancellationToken.None);
                _logger.LogError(ex, "Could not deliver a password reset email.");
            }
        }

        RequestSubmitted = true;
        return Page();
    }
}
