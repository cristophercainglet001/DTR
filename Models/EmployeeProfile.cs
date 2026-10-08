namespace DepEdDTRSystem.Models;

public sealed class EmployeeProfile
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }

    public string? ContactNumber { get; set; }

    public string? Address { get; set; }

    public string? EmergencyContactName { get; set; }

    public string? EmergencyContactNumber { get; set; }

    public byte[]? PhotoData { get; set; }

    public string? PhotoContentType { get; set; }

    public DateTime? PhotoUpdatedAt { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Employee? Employee { get; set; }
}
