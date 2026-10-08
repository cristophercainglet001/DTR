namespace DepEdDTRSystem.Models;

public class School
{
    public int Id { get; set; }

    public string SchoolId { get; set; } = string.Empty;

    public string SchoolName { get; set; } = string.Empty;

    public string DivisionOffice { get; set; } = string.Empty;

    public string RegionOffice { get; set; } = string.Empty;

    public string? SchoolHeadName { get; set; }

    public string? SchoolHeadPosition { get; set; }

    public string Address { get; set; } = string.Empty;

    public string? ContactEmail { get; set; }

    public string? ContactNumber { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<SchoolYear> SchoolYears { get; set; } = new List<SchoolYear>();

    public ICollection<Employee> Employees { get; set; } = new List<Employee>();

    public ICollection<Holiday> Holidays { get; set; } = new List<Holiday>();
}