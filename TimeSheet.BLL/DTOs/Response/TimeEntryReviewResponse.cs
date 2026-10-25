using TimeSheet.DAL.Enums;

namespace TimeSheet.BLL.DTOs.Response
{
    // One row of an entry's review history.
    public class TimeEntryReviewResponse
    {
        public ReviewLevel Level { get; set; }
        public ReviewDecision Decision { get; set; }
        public string? Comment { get; set; }
        public string ReviewerName { get; set; } = null!;
        public DateTime ReviewedAt { get; set; }
    }
}
