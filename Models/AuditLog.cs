namespace DepEdDTRSystem.Models;

public class AuditLog
{
    public long Id { get; set; }

    public int? ActorEmployeeId { get; set; }

    public string Action { get; set; } = string.Empty;

    public string EntityName { get; set; } = string.Empty;

    public string? EntityId { get; set; }

    public string? OldValues { get; set; }

    public string? NewValues { get; set; }

    public string? IpAddress { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Employee? ActorEmployee { get; set; }
}