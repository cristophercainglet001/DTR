namespace DepEdDTRSystem.Models;

public class DtrRecord
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }

    public DateOnly DtrDate { get; set; }

    public TimeOnly? AmTimeIn { get; set; }

    public TimeOnly? AmTimeOut { get; set; }

    public TimeOnly? PmTimeIn { get; set; }

    public TimeOnly? PmTimeOut { get; set; }

    public int UndertimeMinutes { get; set; }

    public string? Remarks { get; set; }

    public string EntrySource { get; set; } = "Manual";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Employee? Employee { get; set; }
}