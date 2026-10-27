using TimeSheet.DAL.Enums;

namespace TimeSheet.BLL.DTOs.Request
{
    // Query-string filters for GET Employee/TimeEntries.
    public class TimeEntryFilterRequest : PagedRequest
    {
        // Cancelled entries are hidden unless status=Cancelled is asked for.
        public TimeEntryStatus? Status { get; set; }

        public DateOnly? From { get; set; }
        public DateOnly? To { get; set; }
        public Guid? ProjectId { get; set; }
    }
}
