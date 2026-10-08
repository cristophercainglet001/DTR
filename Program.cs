using DepEdDTRSystem.Data;
using DepEdDTRSystem.Models;
using DepEdDTRSystem.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.AddRazorPages();
builder.Services.AddMemoryCache();
builder.Services.AddDataProtection()
    .SetApplicationName("DepEdDTRSystem");

builder.Services.AddScoped<IPasswordHasher<AdminAccount>, PasswordHasher<AdminAccount>>();
builder.Services.AddScoped<IPasswordHasher<EmployeeAccount>, PasswordHasher<EmployeeAccount>>();
builder.Services.AddScoped<DtrReportVerificationService>();
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Admin/Login";
        options.AccessDeniedPath = "/Admin/Login";
        options.Cookie.Name = "DepEdDTR.Admin";
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = false;
        options.Events.OnValidatePrincipal = async context =>
        {
            var accountId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            var securityStamp = context.Principal?.FindFirstValue("security_stamp");
            var db = context.HttpContext.RequestServices.GetRequiredService<DtrDbContext>();
            var account = int.TryParse(accountId, out var id)
                ? await db.AdminAccounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id)
                : null;

            if (account is null
                || account.MustChangePassword
                    != (context.Principal?.IsInRole("AdminPasswordChangeRequired") ?? false)
                || !string.Equals(account.SecurityStamp, securityStamp, StringComparison.Ordinal))
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    })
    .AddCookie(EmployeeAuthDefaults.Scheme, options =>
    {
        options.LoginPath = "/Employee/Login";
        options.AccessDeniedPath = "/Employee/Login";
        options.Cookie.Name = EmployeeAuthDefaults.CookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = false;
        options.Events.OnValidatePrincipal = async context =>
        {
            var accountId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            var securityStamp = context.Principal?.FindFirstValue("security_stamp");
            var db = context.HttpContext.RequestServices.GetRequiredService<DtrDbContext>();
            var account = int.TryParse(accountId, out var id)
                ? await db.EmployeeAccounts
                    .AsNoTracking()
                    .Include(x => x.Employee)
                    .FirstOrDefaultAsync(x => x.Id == id)
                : null;

            if (account?.Employee?.Status != "Active"
                || account.MustChangePassword != (context.Principal?.IsInRole("EmployeePasswordChangeRequired") ?? false)
                || !string.Equals(account.SecurityStamp, securityStamp, StringComparison.Ordinal))
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(EmployeeAuthDefaults.Scheme);
            }
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 8,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
});

builder.Services.AddDbContext<DtrDbContext>(options =>
{
    if (!string.IsNullOrWhiteSpace(connectionString))
    {
        options.UseNpgsql(connectionString);
    }
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DtrDbContext>();
    db.Database.Migrate();
    SeedSampleData(db);
    await BootstrapAdminAsync(
        db,
        scope.ServiceProvider.GetRequiredService<IPasswordHasher<AdminAccount>>(),
        builder.Configuration,
        scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("AdminBootstrap"));
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRouting();

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    await next();
});

app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context, next) =>
{
    if (context.User.IsInRole("AdminPasswordChangeRequired")
        && context.Request.Path.StartsWithSegments("/Admin")
        && !context.Request.Path.StartsWithSegments("/Admin/ChangePassword")
        && !context.Request.Path.StartsWithSegments("/Admin/Logout"))
    {
        context.Response.Redirect("/Admin/ChangePassword");
        return;
    }

    await next();
});
app.UseRateLimiter();

app.MapStaticAssets();

app.MapRazorPages();

app.Run();

static async Task BootstrapAdminAsync(
    DtrDbContext db,
    IPasswordHasher<AdminAccount> passwordHasher,
    IConfiguration configuration,
    ILogger logger)
{
    if (await db.AdminAccounts.AnyAsync())
    {
        return;
    }

    var username = configuration["Admin:Username"]?.Trim();
    var password = configuration["Admin:Password"];
    var email = configuration["Admin:Email"]?.Trim();

    if (string.IsNullOrWhiteSpace(username)
        || string.IsNullOrWhiteSpace(password)
        || string.IsNullOrWhiteSpace(email))
    {
        logger.LogWarning(
            "No admin account exists. Configure Admin:Username, Admin:Password, and Admin:Email in user-secrets or environment variables to initialize one.");
        return;
    }

    if (username.Length > 100)
    {
        logger.LogError("Admin:Username must be no longer than 100 characters. The first admin account was not created.");
        return;
    }

    if (password.Length is < 6 or > 24)
    {
        logger.LogError("Admin:Password must be 6-24 characters. The first admin account was not created.");
        return;
    }

    if (email.Length > 254)
    {
        logger.LogError("Admin:Email must be no longer than 254 characters. The first admin account was not created.");
        return;
    }

    try
    {
        _ = new System.Net.Mail.MailAddress(email);
    }
    catch (FormatException)
    {
        logger.LogError("Admin:Email must be a valid email address. The first admin account was not created.");
        return;
    }

    var account = new AdminAccount
    {
        Username = username,
        Email = email,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };
    account.PasswordHash = passwordHasher.HashPassword(account, password);

    db.AdminAccounts.Add(account);
    await db.SaveChangesAsync();
    logger.LogInformation("Initialized the first admin account from protected configuration.");
}

static void SeedSampleData(DtrDbContext db)
{
    if (db.Schools.Any())
    {
        return;
    }

    var school = new School
    {
        SchoolId = "S-001",
        SchoolName = "San Jose Elementary School",
        DivisionOffice = "Division of Pasig City",
        RegionOffice = "NCR",
        SchoolHeadName = "REY R. GUTIERREZ, EdD",
        SchoolHeadPosition = "SCHOOL PRINCIPAL",
        Address = "123 Mabini Street, San Jose, Pasig City",
        ContactEmail = "admin@sanjose-es.edu.ph",
        ContactNumber = "(02) 555-0101",
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    db.Schools.Add(school);
    db.SaveChanges();

    var schoolYear = new SchoolYear
    {
        SchoolId = school.Id,
        Name = "2025-2026",
        StartDate = new DateOnly(2025, 6, 1),
        EndDate = new DateOnly(2026, 3, 31),
        IsCurrent = true,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    db.SchoolYears.Add(schoolYear);

    var employees = new List<Employee>
    {
        new Employee
        {
            SchoolId = school.Id,
            EmployeeId = "EMP-1001",
            FirstName = "Maria",
            MiddleName = "Dela Cruz",
            LastName = "Santos",
            Position = "Teacher I",
            Department = "Elementary",
            Email = "maria.santos@sanjose-es.edu.ph",
            Status = "Active",
            DateHired = new DateOnly(2021, 8, 2),
        },
        new Employee
        {
            SchoolId = school.Id,
            EmployeeId = "EMP-1002",
            FirstName = "Juan",
            MiddleName = "Ramos",
            LastName = "Garcia",
            Position = "Administrative Assistant",
            Department = "Office",
            Email = "juan.garcia@sanjose-es.edu.ph",
            Status = "Active",
            DateHired = new DateOnly(2019, 1, 15),
        },
        new Employee
        {
            SchoolId = school.Id,
            EmployeeId = "EMP-1003",
            FirstName = "Ana",
            MiddleName = "Marquez",
            LastName = "Lopez",
            Position = "Principal",
            Department = "Administration",
            Email = "ana.lopez@sanjose-es.edu.ph",
            Status = "Active",
            DateHired = new DateOnly(2016, 4, 10),
        },
    };

    db.Employees.AddRange(employees);
    db.SaveChanges();

    var employee1 = employees[0];
    var employee2 = employees[1];
    var employee3 = employees[2];

    db.DtrRecords.AddRange(
        new DtrRecord
        {
            EmployeeId = employee1.Id,
            DtrDate = new DateOnly(2026, 10, 6),
            AmTimeIn = new TimeOnly(7, 30),
            AmTimeOut = new TimeOnly(11, 30),
            PmTimeIn = new TimeOnly(12, 30),
            PmTimeOut = new TimeOnly(16, 30),
            UndertimeMinutes = 0,
            Remarks = "On time",
            EntrySource = "Manual",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        },
        new DtrRecord
        {
            EmployeeId = employee2.Id,
            DtrDate = new DateOnly(2026, 10, 6),
            AmTimeIn = new TimeOnly(8, 0),
            AmTimeOut = new TimeOnly(12, 0),
            PmTimeIn = new TimeOnly(13, 0),
            PmTimeOut = new TimeOnly(17, 0),
            UndertimeMinutes = 0,
            Remarks = "Regular office schedule",
            EntrySource = "Manual",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });

    db.Holidays.AddRange(
        new Holiday
        {
            SchoolId = school.Id,
            HolidayDate = new DateOnly(2026, 10, 9),
            HolidayName = "Araw ng Kagitingan",
            HolidayType = "National Holiday",
            Remarks = "Regular holiday",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        },
        new Holiday
        {
            SchoolId = school.Id,
            HolidayDate = new DateOnly(2026, 11, 30),
            HolidayName = "Bonifacio Day",
            HolidayType = "National Holiday",
            Remarks = "Regular holiday",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });

    db.LeaveApplications.AddRange(
        new LeaveApplication
        {
            EmployeeId = employee1.Id,
            LeaveType = "Sick Leave",
            StartDate = new DateOnly(2026, 10, 12),
            EndDate = new DateOnly(2026, 10, 13),
            NumberOfDays = 2m,
            Reason = "Recovering from mild fever",
            Status = "Approved",
            ReviewedByEmployeeId = employee3.Id,
            ReviewedAt = DateTime.UtcNow,
            Remarks = "Approved by principal",
            FiledAt = DateTime.UtcNow.AddDays(-1),
            UpdatedAt = DateTime.UtcNow,
        },
        new LeaveApplication
        {
            EmployeeId = employee2.Id,
            LeaveType = "Personal Leave",
            StartDate = new DateOnly(2026, 10, 15),
            EndDate = new DateOnly(2026, 10, 15),
            NumberOfDays = 1m,
            Reason = "Family appointment",
            Status = "Pending",
            FiledAt = DateTime.UtcNow.AddDays(-2),
            UpdatedAt = DateTime.UtcNow,
        });

    db.AuditLogs.AddRange(
        new AuditLog
        {
            ActorEmployeeId = employee3.Id,
            Action = "Create",
            EntityName = "School",
            EntityId = school.Id.ToString(),
            NewValues = "{ \"SchoolName\": \"San Jose Elementary School\" }",
            IpAddress = "127.0.0.1",
            CreatedAt = DateTime.UtcNow,
        },
        new AuditLog
        {
            ActorEmployeeId = employee1.Id,
            Action = "Login",
            EntityName = "Employee",
            EntityId = employee1.Id.ToString(),
            NewValues = "{ \"Status\": \"Active\" }",
            IpAddress = "127.0.0.1",
            CreatedAt = DateTime.UtcNow,
        });

    db.SaveChanges();
}