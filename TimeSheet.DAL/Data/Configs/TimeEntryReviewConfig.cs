using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TimeSheet.DAL.Models;

namespace TimeSheet.DAL.Data.Configs
{
    public class TimeEntryReviewConfig : IEntityTypeConfiguration<TimeEntryReview>
    {
        public void Configure(EntityTypeBuilder<TimeEntryReview> builder)
        {
            builder.ToTable("TimeEntryReviews", t =>
            {
                // Rejected (2) and Reopened (3) must carry a non-blank comment.
                t.HasCheckConstraint("CK_TimeEntryReviews_Comment_Required",
                    "[Decision] = 1 OR ([Comment] IS NOT NULL AND LEN(TRIM([Comment])) > 0)");
            });
            builder.HasKey(r => r.Id);

            builder.Property(r => r.Comment).HasMaxLength(1000);

            builder.Property(r => r.Level)
                .HasConversion<int>()
                .IsRequired();

            builder.Property(r => r.Decision)
                .HasConversion<int>()
                .IsRequired();

            // History + "latest review" lookups.
            builder.HasIndex(r => new { r.TimeEntryId, r.CreatedAt });

            builder.HasOne(r => r.TimeEntry)
                .WithMany(e => e.Reviews)
                .HasForeignKey(r => r.TimeEntryId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(r => r.Reviewer)
                .WithMany()
                .HasForeignKey(r => r.ReviewerId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
