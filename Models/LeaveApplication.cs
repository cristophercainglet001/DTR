namespace DepEdDTRSystem.Models;

public class LeaveApplication
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }

    public string LeaveType { get; set; } = string.Empty;

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    public decimal NumberOfDays { get; set; }

    public string Reason { get; set; } = string.Empty;

    public string Status { get; set; } = "Pending";

    public int? ReviewedByEmployeeId { get; set; }

    public DateTime? ReviewedAt { get; set; }

    public string? Remarks { get; set; }

    public DateTime FiledAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Employee? Employee { get; set; }

    public Employee? ReviewedByEmployee { get; set; }
}