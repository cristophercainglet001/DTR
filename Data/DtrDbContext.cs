using DepEdDTRSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace DepEdDTRSystem.Data;

public class DtrDbContext : DbContext
{
    public DtrDbContext(DbContextOptions<DtrDbContext> options)
        : base(options)
    {
    }

    public DbSet<School> Schools => Set<School>();

    public DbSet<SchoolYear> SchoolYears => Set<SchoolYear>();

    public DbSet<Employee> Employees => Set<Employee>();

    public DbSet<DtrRecord> DtrRecords => Set<DtrRecord>();

    public DbSet<Holiday> Holidays => Set<Holiday>();

    public DbSet<LeaveApplication> LeaveApplications => Set<LeaveApplication>();

    public DbSet<SeminarApplication> SeminarApplications => Set<SeminarApplication>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<AdminAccount> AdminAccounts => Set<AdminAccount>();

    public DbSet<EmployeeAccount> EmployeeAccounts => Set<EmployeeAccount>();

    public DbSet<EmployeeProfile> EmployeeProfiles => Set<EmployeeProfile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AdminAccount>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Username)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(x => x.Email)
                .HasMaxLength(254)
                .IsRequired();

            entity.Property(x => x.PasswordHash)
                .HasMaxLength(500)
                .IsRequired();

            entity.Property(x => x.MustChangePassword)
                .HasDefaultValue(false);

            entity.Property(x => x.PasswordResetTokenHash)
                .HasMaxLength(64);

            entity.Property(x => x.SecurityStamp)
                .HasMaxLength(32)
                .IsRequired();

            entity.HasIndex(x => x.Username)
                .IsUnique();
        });

        modelBuilder.Entity<EmployeeAccount>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Email).HasMaxLength(254).IsRequired();
            entity.Property(x => x.NormalizedEmail).HasMaxLength(254).IsRequired();
            entity.Property(x => x.PasswordHash).HasMaxLength(500);
            entity.Property(x => x.MustChangePassword).HasDefaultValue(false);
            entity.Property(x => x.InvitationTokenHash).HasMaxLength(64);
            entity.Property(x => x.SecurityStamp).HasMaxLength(32).IsRequired();
            entity.HasIndex(x => x.EmployeeId).IsUnique();
            entity.HasIndex(x => x.NormalizedEmail).IsUnique();
            entity.HasOne(x => x.Employee)
                .WithOne()
                .HasForeignKey<EmployeeAccount>(x => x.EmployeeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EmployeeProfile>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.ContactNumber).HasMaxLength(20);
            entity.Property(x => x.Address).HasMaxLength(300);
            entity.Property(x => x.EmergencyContactName).HasMaxLength(100);
            entity.Property(x => x.EmergencyContactNumber).HasMaxLength(20);
            entity.Property(x => x.PhotoContentType).HasMaxLength(50);
            entity.HasIndex(x => x.EmployeeId).IsUnique();
            entity.HasOne(x => x.Employee)
                .WithOne()
                .HasForeignKey<EmployeeProfile>(x => x.EmployeeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<School>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.SchoolId)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(x => x.SchoolName)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(x => x.DivisionOffice)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(x => x.RegionOffice)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(x => x.SchoolHeadName)
                .HasMaxLength(150);

            entity.Property(x => x.SchoolHeadPosition)
                .HasMaxLength(150);

            entity.Property(x => x.Address)
                .HasMaxLength(300)
                .IsRequired();

            entity.Property(x => x.ContactEmail)
                .HasMaxLength(200);

            entity.Property(x => x.ContactNumber)
                .HasMaxLength(50);

            entity.HasIndex(x => x.SchoolId)
                .IsUnique();
        });

        modelBuilder.Entity<SchoolYear>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .HasMaxLength(20)
                .IsRequired();

            entity.HasIndex(x => new
            {
                x.SchoolId,
                x.Name
            })
            .IsUnique();

            entity.HasOne(x => x.School)
                .WithMany(x => x.SchoolYears)
                .HasForeignKey(x => x.SchoolId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Employee>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.EmployeeId)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(x => x.FirstName)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(x => x.MiddleName)
                .HasMaxLength(100);

            entity.Property(x => x.LastName)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(x => x.Suffix)
                .HasMaxLength(20);

            entity.Property(x => x.Position)
                .HasMaxLength(150)
                .IsRequired();

            entity.Property(x => x.Department)
                .HasMaxLength(150);

            entity.Property(x => x.Email)
                .HasMaxLength(200);

            entity.Property(x => x.Status)
                .HasMaxLength(30)
                .IsRequired();

            entity.HasIndex(x => x.EmployeeId)
                .IsUnique();

            entity.HasIndex(x => x.LastName);

            entity.HasIndex(x => x.Department);

            entity.HasOne(x => x.School)
                .WithMany(x => x.Employees)
                .HasForeignKey(x => x.SchoolId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DtrRecord>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.EntrySource)
                .HasMaxLength(30)
                .IsRequired();

            entity.Property(x => x.Remarks)
                .HasMaxLength(500);

            entity.HasIndex(x => new
            {
                x.EmployeeId,
                x.DtrDate
            })
            .IsUnique();

            entity.HasOne(x => x.Employee)
                .WithMany(x => x.DtrRecords)
                .HasForeignKey(x => x.EmployeeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Holiday>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.HolidayName)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(x => x.HolidayType)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(x => x.Remarks)
                .HasMaxLength(500);

            entity.HasIndex(x => new
            {
                x.SchoolId,
                x.HolidayDate
            })
            .IsUnique();

            entity.HasOne(x => x.School)
                .WithMany(x => x.Holidays)
                .HasForeignKey(x => x.SchoolId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LeaveApplication>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.LeaveType)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(x => x.NumberOfDays)
                .HasPrecision(5, 2);

            entity.Property(x => x.Reason)
                .HasMaxLength(1000)
                .IsRequired();

            entity.Property(x => x.Status)
                .HasMaxLength(30)
                .IsRequired();

            entity.Property(x => x.Remarks)
                .HasMaxLength(1000);

            entity.HasIndex(x => x.EmployeeId);

            entity.HasIndex(x => x.Status);

            entity.HasOne(x => x.Employee)
                .WithMany(x => x.LeaveApplications)
                .HasForeignKey(x => x.EmployeeId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.ReviewedByEmployee)
                .WithMany()
                .HasForeignKey(x => x.ReviewedByEmployeeId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SeminarApplication>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SeminarTitle).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Organizer).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Venue).HasMaxLength(300).IsRequired();
            entity.Property(x => x.Purpose).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Remarks).HasMaxLength(1000);
            entity.HasIndex(x => x.EmployeeId);
            entity.HasIndex(x => x.Status);
            entity.HasOne(x => x.Employee)
                .WithMany(x => x.SeminarApplications)
                .HasForeignKey(x => x.EmployeeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Action)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(x => x.EntityName)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(x => x.EntityId)
                .HasMaxLength(100);

            entity.Property(x => x.IpAddress)
                .HasMaxLength(100);

            entity.HasIndex(x => x.CreatedAt);

            entity.HasIndex(x => x.ActorEmployeeId);

            entity.HasOne(x => x.ActorEmployee)
                .WithMany()
                .HasForeignKey(x => x.ActorEmployeeId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}