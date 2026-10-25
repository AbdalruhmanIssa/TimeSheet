using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TimeSheet.DAL.Models;

namespace TimeSheet.DAL.Data.Configs
{
    public class ProjectAssignmentConfig : IEntityTypeConfiguration<ProjectAssignment>
    {
        public void Configure(EntityTypeBuilder<ProjectAssignment> builder)
        {
            builder.ToTable("ProjectAssignments");
            builder.HasKey(a => a.Id);

            // One row per employee per project; re-assigning reactivates it.
            builder.HasIndex(a => new { a.ProjectId, a.EmployeeId }).IsUnique();

            builder.HasOne(a => a.Project)
                .WithMany(p => p.Assignments)
                .HasForeignKey(a => a.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(a => a.Employee)
                .WithMany()
                .HasForeignKey(a => a.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
