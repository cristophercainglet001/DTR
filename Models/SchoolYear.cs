namespace DepEdDTRSystem.Models;

public class SchoolYear
{
    public int Id { get; set; }

    public int SchoolId { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    public bool IsCurrent { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public School? School { get; set; }
}