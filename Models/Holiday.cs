namespace DepEdDTRSystem.Models;

public class Holiday
{
    public int Id { get; set; }

    public int SchoolId { get; set; }

    public DateOnly HolidayDate { get; set; }

    public string HolidayName { get; set; } = string.Empty;

    public string HolidayType { get; set; } = string.Empty;

    public string? Remarks { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public School? School { get; set; }
}