using System.ComponentModel.DataAnnotations;
using DepEdDTRSystem.Data;
using DepEdDTRSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DepEdDTRSystem.Pages.Admin;

[Authorize(Roles = "Admin")]
public sealed class SchoolsModel : PageModel
{
    private readonly DtrDbContext _db;

    public SchoolsModel(DtrDbContext db) => _db = db;

    [BindProperty]
    public SchoolInput Input { get; set; } = new();

    public IReadOnlyList<School> Schools { get; private set; } = [];

    [TempData]
    public string? StatusMessage { get; set; }

    public Task OnGetAsync(CancellationToken cancellationToken) =>
        LoadSchoolsAsync(cancellationToken);

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        Input.SchoolName = Input.SchoolName?.Trim();
        Input.SchoolHeadName = Input.SchoolHeadName?.Trim();
        Input.SchoolHeadPosition = Input.SchoolHeadPosition?.Trim();

        if (ModelState.IsValid)
        {
            var school = await _db.Schools
                .FirstOrDefaultAsync(x => x.Id == Input.SchoolId, cancellationToken);

            if (school is null)
            {
                ModelState.AddModelError(string.Empty, "That school could not be found.");
            }
            else
            {
                school.SchoolName = Input.SchoolName!;
                school.SchoolHeadName = Input.SchoolHeadName;
                school.SchoolHeadPosition = Input.SchoolHeadPosition;
                school.UpdatedAt = DateTime.UtcNow;

                await _db.SaveChangesAsync(cancellationToken);
                StatusMessage = $"School details for {school.SchoolName} were saved.";
                return RedirectToPage();
            }
        }

        await LoadSchoolsAsync(cancellationToken);
        return Page();
    }

    private async Task LoadSchoolsAsync(CancellationToken cancellationToken)
    {
        Schools = await _db.Schools
            .AsNoTracking()
            .OrderBy(x => x.SchoolName)
            .ToListAsync(cancellationToken);
    }

    public sealed class SchoolInput
    {
        public int SchoolId { get; set; }

        [Required]
        [StringLength(200)]
        public string? SchoolName { get; set; }

        [Required]
        [StringLength(150)]
        public string? SchoolHeadName { get; set; }

        [Required]
        [StringLength(150)]
        public string? SchoolHeadPosition { get; set; }
    }
}
