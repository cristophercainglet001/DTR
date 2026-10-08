namespace DepEdDTRSystem.Models;

public sealed class SeminarApplication
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }

    public string SeminarTitle { get; set; } = string.Empty;

    public string Organizer { get; set; } = string.Empty;

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    public string Venue { get; set; } = string.Empty;

    public string Purpose { get; set; } = string.Empty;

    public string Status { get; set; } = "Pending";

    public string? Remarks { get; set; }

    public DateTime? ReviewedAt { get; set; }

    public DateTime FiledAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Employee? Employee { get; set; }
}
