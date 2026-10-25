using TimeSheet.DAL.Enums;

namespace TimeSheet.DAL.Models
{
    // Hours an employee logged on one project for one day.
    public class TimeEntry : BaseEntity
    {
        public Guid ProjectId { get; set; }
        public Project Project { get; set; } = null!;

        public string EmployeeId { get; set; } = null!;
        public ApplicationUser Employee { get; set; } = null!;

        public DateOnly WorkDate { get; set; }
        public decimal Hours { get; set; }

        // Multiple tasks go in one entry's notes.
        public string? Notes { get; set; }

        public TimeEntryStatus Status { get; set; } = TimeEntryStatus.PendingManager;

        // Updated on resubmission.
        public DateTime SubmittedAt { get; set; }

        // Concurrency token: a second reviewer gets "already reviewed".
        public byte[] RowVersion { get; set; } = null!;

        // Review history (manager/director approvals, rejections, reopens).
        public ICollection<TimeEntryReview> Reviews { get; set; } = new List<TimeEntryReview>();
    }
}
