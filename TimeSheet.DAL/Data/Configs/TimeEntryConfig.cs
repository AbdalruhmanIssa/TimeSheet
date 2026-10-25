using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TimeSheet.DAL.Models;

namespace TimeSheet.DAL.Data.Configs
{
    public class TimeEntryConfig : IEntityTypeConfiguration<TimeEntry>
    {
        public void Configure(EntityTypeBuilder<TimeEntry> builder)
        {
            builder.ToTable("TimeEntries", t =>
            {
                t.HasCheckConstraint("CK_TimeEntries_Hours_Range", "[Hours] > 0 AND [Hours] <= 12");
            });
            builder.HasKey(e => e.Id);

            builder.Property(e => e.Hours).HasPrecision(5, 2);
            builder.Property(e => e.Notes).HasMaxLength(1000);

            builder.Property(e => e.Status)
                .HasConversion<int>()
                .IsRequired();

            builder.Property(e => e.RowVersion).IsRowVersion();

            // Reports
            builder.HasIndex(e => new { e.ProjectId, e.Status, e.WorkDate });

            // "My entries" + daily cap check
            builder.HasIndex(e => new { e.EmployeeId, e.WorkDate });

            // One entry per employee per project per day (Cancelled entries don't count).
            builder.HasIndex(e => new { e.EmployeeId, e.ProjectId, e.WorkDate })
                .IsUnique()
                .HasFilter("[Status] <> 4")
                .HasDatabaseName("UX_TimeEntries_Employee_Project_WorkDate");

            builder.HasOne(e => e.Project)
                .WithMany()
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(e => e.Employee)
                .WithMany()
                .HasForeignKey(e => e.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
