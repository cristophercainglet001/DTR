using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using DepEdDTRSystem.Data;
using DepEdDTRSystem.Models;
using DepEdDTRSystem.Services;
using DepEdDTRSystem.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DepEdDTRSystem.Pages.Employee;

[Authorize(AuthenticationSchemes = EmployeeAuthDefaults.Scheme, Roles = "Employee")]
[RequestSizeLimit(4 * 1024 * 1024)]
public sealed class ProfileModel : PageModel
{
    private const int MaxPhotoBytes = 2 * 1024 * 1024;
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private const string PhoneRegex = @"^[0-9+()\-\s]{7,20}$";

    private readonly DtrDbContext _db;
    private readonly IPasswordHasher<EmployeeAccount> _passwordHasher;

    public ProfileModel(DtrDbContext db, IPasswordHasher<EmployeeAccount> passwordHasher)
    {
        _db = db;
        _passwordHasher = passwordHasher;
    }

    public sealed class InfoInput
    {
        [Display(Name = "Contact number")]
        [StringLength(20), RegularExpression(PhoneRegex, ErrorMessage = "Enter a valid phone number.")]
        public string? ContactNumber { get; set; }

        [StringLength(300)]
        [Display(Name = "Home address")]
        public string? Address { get; set; }

        [StringLength(100)]
        [Display(Name = "Emergency contact name")]
        public string? EmergencyContactName { get; set; }

        [Display(Name = "Emergency contact number")]
        [StringLength(20), RegularExpression(PhoneRegex, ErrorMessage = "Enter a valid phone number.")]
        public string? EmergencyContactNumber { get; set; }
    }

    public sealed class PasswordInput
    {
        [Required, StringLength(128), DataType(DataType.Password)]
        [Display(Name = "Current password")]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required, StringLength(24, MinimumLength = 6,
            ErrorMessage = "Use a password between 6 and 24 characters."), DataType(DataType.Password)]
        [Display(Name = "New password")]
        public string NewPassword { get; set; } = string.Empty;

        [Required, Compare(nameof(NewPassword), ErrorMessage = "The passwords do not match."),
            DataType(DataType.Password)]
        [Display(Name = "Confirm new password")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    [BindProperty]
    public InfoInput Info { get; set; } = new();

    [BindProperty]
    public PasswordInput Password { get; set; } = new();

    public Models.Employee Employee { get; private set; } = null!;

    public EmployeeAvatarViewModel Avatar { get; private set; } = null!;

    public bool HasPhoto { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public string? ErrorMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var profile = await LoadAsync(cancellationToken);
        Info = new InfoInput
        {
            ContactNumber = profile?.ContactNumber,
            Address = profile?.Address,
            EmergencyContactName = profile?.EmergencyContactName,
            EmergencyContactNumber = profile?.EmergencyContactNumber,
        };
        return Page();
    }

    public async Task<IActionResult> OnGetPhotoAsync(CancellationToken cancellationToken)
    {
        var employeeId = GetEmployeeId();
        var photo = await _db.EmployeeProfiles
            .AsNoTracking()
            .Where(x => x.EmployeeId == employeeId && x.PhotoData != null)
            .Select(x => new { x.PhotoData, x.PhotoContentType })
            .FirstOrDefaultAsync(cancellationToken);

        if (photo?.PhotoData is null || photo.PhotoContentType is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "private, max-age=86400";
        Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
        return File(photo.PhotoData, photo.PhotoContentType);
    }

    public async Task<IActionResult> OnPostInfoAsync(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        KeepValidationFor(nameof(Info));
        if (!ModelState.IsValid)
        {
            await LoadAsync(cancellationToken);
            return Page();
        }

        var profile = await GetOrCreateProfileAsync(cancellationToken);
        profile.ContactNumber = Clean(Info.ContactNumber);
        profile.Address = Clean(Info.Address);
        profile.EmergencyContactName = Clean(Info.EmergencyContactName);
        profile.EmergencyContactNumber = Clean(Info.EmergencyContactNumber);
        profile.UpdatedAt = DateTime.UtcNow;
        AddAudit("ProfileUpdated");
        await _db.SaveChangesAsync(cancellationToken);

        StatusMessage = "Your information has been saved.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPhotoAsync(IFormFile? photoFile, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (photoFile is null || photoFile.Length == 0)
        {
            return await RejectAsync("Choose a photo to upload.", cancellationToken);
        }

        if (photoFile.Length > MaxPhotoBytes)
        {
            return await RejectAsync("The photo must be 2 MB or smaller.", cancellationToken);
        }

        await using var stream = photoFile.OpenReadStream();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();

        var contentType = DetectImageType(bytes);
        if (contentType is null)
        {
            return await RejectAsync("Use a JPG, PNG or WebP image.", cancellationToken);
        }

        var profile = await GetOrCreateProfileAsync(cancellationToken);
        profile.PhotoData = bytes;
        profile.PhotoContentType = contentType;
        profile.PhotoUpdatedAt = DateTime.UtcNow;
        profile.UpdatedAt = DateTime.UtcNow;
        AddAudit("ProfilePhotoUpdated");
        await _db.SaveChangesAsync(cancellationToken);

        StatusMessage = "Your photo has been updated.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemovePhotoAsync(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var employeeId = GetEmployeeId();
        var profile = await _db.EmployeeProfiles
            .FirstOrDefaultAsync(x => x.EmployeeId == employeeId, cancellationToken);
        if (profile?.PhotoData is not null)
        {
            profile.PhotoData = null;
            profile.PhotoContentType = null;
            profile.PhotoUpdatedAt = null;
            profile.UpdatedAt = DateTime.UtcNow;
            AddAudit("ProfilePhotoRemoved");
            await _db.SaveChangesAsync(cancellationToken);
        }

        StatusMessage = "Your photo has been removed.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPasswordAsync(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        KeepValidationFor(nameof(Password));
        if (!ModelState.IsValid)
        {
            await LoadAsync(cancellationToken);
            return Page();
        }

        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var accountId))
        {
            return Forbid();
        }

        var account = await _db.EmployeeAccounts
            .Include(x => x.Employee)
            .FirstOrDefaultAsync(x => x.Id == accountId, cancellationToken);
        if (account?.Employee?.Status != "Active" || account.PasswordHash is null)
        {
            return Forbid();
        }

        if (account.LockoutEnd > DateTime.UtcNow)
        {
            ModelState.AddModelError("Password.CurrentPassword", "Too many attempts. Please try again later.");
            await LoadAsync(cancellationToken);
            return Page();
        }

        var now = DateTime.UtcNow;
        if (_passwordHasher.VerifyHashedPassword(account, account.PasswordHash, Password.CurrentPassword)
            == PasswordVerificationResult.Failed)
        {
            account.FailedLoginAttempts++;
            if (account.FailedLoginAttempts >= MaxFailedAttempts)
            {
                account.FailedLoginAttempts = 0;
                account.LockoutEnd = now.Add(LockoutDuration);
            }
            account.UpdatedAt = now;
            await _db.SaveChangesAsync(cancellationToken);

            ModelState.AddModelError("Password.CurrentPassword", "The current password is incorrect.");
            await LoadAsync(cancellationToken);
            return Page();
        }

        if (string.Equals(Password.CurrentPassword, Password.NewPassword, StringComparison.Ordinal))
        {
            ModelState.AddModelError("Password.NewPassword", "Choose a password different from the current one.");
            await LoadAsync(cancellationToken);
            return Page();
        }

        account.PasswordHash = _passwordHasher.HashPassword(account, Password.NewPassword);
        account.MustChangePassword = false;
        account.FailedLoginAttempts = 0;
        account.LockoutEnd = null;
        account.SecurityStamp = Guid.NewGuid().ToString("N");
        account.UpdatedAt = now;
        AddAudit("PasswordChanged");
        await _db.SaveChangesAsync(cancellationToken);

        // The new security stamp invalidates the current cookie, so reissue it.
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, account.Id.ToString()),
            new Claim(ClaimTypes.Name, account.Employee.EmployeeId),
            new Claim(ClaimTypes.Email, account.Email),
            new Claim(ClaimTypes.Role, "Employee"),
            new Claim("employee_id", account.EmployeeId.ToString()),
            new Claim("security_stamp", account.SecurityStamp),
        };
        await HttpContext.SignInAsync(
            EmployeeAuthDefaults.Scheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, EmployeeAuthDefaults.Scheme)),
            new AuthenticationProperties
            {
                IsPersistent = false,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8),
            });

        StatusMessage = "Your password has been changed.";
        return RedirectToPage();
    }

    // Each form posts only its own fields; unposted fields of the other form surface
    // as unprefixed "required" errors, so keep only the posted form's validation results.
    private void KeepValidationFor(string prefix)
    {
        foreach (var key in ModelState.Keys
                     .Where(k => !k.StartsWith(prefix + ".", StringComparison.Ordinal))
                     .ToList())
        {
            ModelState.Remove(key);
        }
    }

    private async Task<IActionResult> RejectAsync(string message, CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
        ErrorMessage = message;
        return Page();
    }

    private async Task<EmployeeProfile?> LoadAsync(CancellationToken cancellationToken)
    {
        var employeeId = GetEmployeeId();
        Employee = await _db.Employees
            .AsNoTracking()
            .Include(x => x.School)
            .FirstOrDefaultAsync(x => x.Id == employeeId && x.Status == "Active", cancellationToken)
            ?? throw new InvalidOperationException("The signed-in employee record is not active or no longer exists.");

        var profile = await _db.EmployeeProfiles
            .AsNoTracking()
            .Where(x => x.EmployeeId == employeeId)
            .Select(x => new EmployeeProfile
            {
                ContactNumber = x.ContactNumber,
                Address = x.Address,
                EmergencyContactName = x.EmergencyContactName,
                EmergencyContactNumber = x.EmergencyContactNumber,
                PhotoUpdatedAt = x.PhotoData != null ? x.PhotoUpdatedAt : null,
            })
            .FirstOrDefaultAsync(cancellationToken);

        HasPhoto = profile?.PhotoUpdatedAt is not null;
        Avatar = EmployeeDisplay.CreateAvatar(
            Employee,
            HasPhoto ? Url.Page("/Employee/Profile", "Photo", new { v = profile!.PhotoUpdatedAt!.Value.Ticks }) : null,
            "xl");
        return profile;
    }

    private async Task<EmployeeProfile> GetOrCreateProfileAsync(CancellationToken cancellationToken)
    {
        var employeeId = GetEmployeeId();
        var profile = await _db.EmployeeProfiles
            .FirstOrDefaultAsync(x => x.EmployeeId == employeeId, cancellationToken);
        if (profile is null)
        {
            profile = new EmployeeProfile { EmployeeId = employeeId };
            _db.EmployeeProfiles.Add(profile);
        }

        return profile;
    }

    private int GetEmployeeId() =>
        int.TryParse(User.FindFirstValue("employee_id"), out var id)
            ? id
            : throw new InvalidOperationException("The signed-in employee identity is missing its employee ID.");

    private void AddAudit(string action)
    {
        var employeeId = GetEmployeeId();
        _db.AuditLogs.Add(new AuditLog
        {
            Action = action,
            EntityName = "EmployeeProfile",
            EntityId = employeeId.ToString(),
            ActorEmployeeId = employeeId,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            CreatedAt = DateTime.UtcNow,
        });
    }

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = new string(value.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return cleaned.Length == 0 ? null : cleaned;
    }

    private static string? DetectImageType(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (bytes.Length >= 8
            && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return "image/png";
        }

        if (bytes.Length >= 12
            && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8)
            && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        return null;
    }
}
