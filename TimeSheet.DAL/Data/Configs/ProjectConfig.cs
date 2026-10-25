using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TimeSheet.DAL.Models;

namespace TimeSheet.DAL.Data.Configs
{
    public class ProjectConfig : IEntityTypeConfiguration<Project>
    {
        public void Configure(EntityTypeBuilder<Project> builder)
        {
            builder.ToTable("Projects", t =>
            {
                t.HasCheckConstraint("CK_Projects_BudgetHours_Positive", "[BudgetHours] > 0");
                t.HasCheckConstraint("CK_Projects_Manager_Not_Director", "[ManagerId] <> [DirectorId]");
                t.HasCheckConstraint("CK_Projects_EndDate_After_StartDate", "[EndDate] IS NULL OR [EndDate] >= [StartDate]");
            });
            builder.HasKey(p => p.Id);

            builder.Property(p => p.Code)
                .IsRequired()
                .HasMaxLength(20);
            builder.HasIndex(p => p.Code).IsUnique();

            builder.Property(p => p.Name)
                .IsRequired()
                .HasMaxLength(200);

            builder.Property(p => p.Description).HasMaxLength(2000);

            builder.Property(p => p.BudgetHours).HasPrecision(9, 2);

            builder.Property(p => p.Status)
                .HasConversion<int>()
                .IsRequired();

            builder.HasOne(p => p.Client)
                .WithMany()
                .HasForeignKey(p => p.ClientId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(p => p.Director)
                .WithMany()
                .HasForeignKey(p => p.DirectorId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(p => p.Manager)
                .WithMany()
                .HasForeignKey(p => p.ManagerId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
