namespace DepEdDTRSystem.Models;

public sealed class AdminAccount
{
    public int Id { get; set; }

    public string Username { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public bool MustChangePassword { get; set; }

    public int FailedLoginAttempts { get; set; }

    public DateTime? LockoutEnd { get; set; }

    public string? PasswordResetTokenHash { get; set; }

    public DateTime? PasswordResetTokenExpiresAt { get; set; }

    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
