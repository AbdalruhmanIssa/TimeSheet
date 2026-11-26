namespace TimeSheet.BLL.DTOs.Request
{
    // Query-string filters for GET Director/TimeEntries/approved (entries the director can reopen).
    public class ApprovedEntryFilterRequest : PagedRequest
    {
        public Guid? ProjectId { get; set; }
        public string? EmployeeId { get; set; }

        // WorkDate range, inclusive.
        public DateOnly? From { get; set; }
        public DateOnly? To { get; set; }
    }
}
