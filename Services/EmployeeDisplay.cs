using DepEdDTRSystem.Models;
using DepEdDTRSystem.ViewModels;

namespace DepEdDTRSystem.Services;

public static class EmployeeDisplay
{
    public static EmployeeAvatarViewModel CreateAvatar(Employee employee, string? photoUrl, string size = "md")
    {
        var first = employee.FirstName.Trim();
        var last = employee.LastName.Trim();
        var initials = $"{(first.Length > 0 ? first[0] : '?')}{(last.Length > 0 ? last[0] : string.Empty)}"
            .ToUpperInvariant();
        return new EmployeeAvatarViewModel(initials, photoUrl, size);
    }
}
