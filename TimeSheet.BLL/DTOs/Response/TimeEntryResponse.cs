using TimeSheet.DAL.Enums;

namespace TimeSheet.BLL.DTOs.Response
{
    public class TimeEntryResponse
    {
        public Guid Id { get; set; }

        public Guid ProjectId { get; set; }
        public string ProjectCode { get; set; } = null!;
        public string ProjectName { get; set; } = null!;

        public DateOnly WorkDate { get; set; }
        public decimal Hours { get; set; }
        public string? Notes { get; set; }

        public TimeEntryStatus Status { get; set; }
        public DateTime SubmittedAt { get; set; }

        // Only when Status is Rejected — taken from the latest review row
        // (a reopened entry shows Director + the reopen comment).
        public ReviewLevel? RejectedAtLevel { get; set; }
        public string? RejectionComment { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
