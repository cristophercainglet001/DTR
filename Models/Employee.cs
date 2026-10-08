namespace DepEdDTRSystem.Models;

public class Employee
{
    public int Id { get; set; }

    public int SchoolId { get; set; }

    public string EmployeeId { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;

    public string? MiddleName { get; set; }

    public string LastName { get; set; } = string.Empty;

    public string? Suffix { get; set; }

    public string Position { get; set; } = string.Empty;

    public string? Department { get; set; }

    public string? Email { get; set; }

    public string Status { get; set; } = "Active";

    public DateOnly? DateHired { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public School? School { get; set; }

    public ICollection<DtrRecord> DtrRecords { get; set; } = new List<DtrRecord>();

    public ICollection<LeaveApplication> LeaveApplications { get; set; } = new List<LeaveApplication>();

    public ICollection<SeminarApplication> SeminarApplications { get; set; } = new List<SeminarApplication>();
}