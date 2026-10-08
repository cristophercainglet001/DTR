namespace DepEdDTRSystem.Models;

public sealed class EmployeeAccount
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }

    public string Email { get; set; } = string.Empty;

    public string NormalizedEmail { get; set; } = string.Empty;

    public string? PasswordHash { get; set; }

    public bool MustChangePassword { get; set; }

    public int FailedLoginAttempts { get; set; }

    public DateTime? LockoutEnd { get; set; }

    public string? InvitationTokenHash { get; set; }

    public DateTime? InvitationExpiresAt { get; set; }

    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Employee? Employee { get; set; }
}
