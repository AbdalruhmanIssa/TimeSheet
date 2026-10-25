using TimeSheet.DAL.Enums;

namespace TimeSheet.DAL.Models
{
    // One row per review action (history). CreatedAt = when the review happened.
    public class TimeEntryReview : BaseEntity
    {
        public Guid TimeEntryId { get; set; }
        public TimeEntry TimeEntry { get; set; } = null!;

        public string ReviewerId { get; set; } = null!;
        public ApplicationUser Reviewer { get; set; } = null!;

        public ReviewLevel Level { get; set; }
        public ReviewDecision Decision { get; set; }

        // Required for Rejected and Reopened.
        public string? Comment { get; set; }
    }
}
