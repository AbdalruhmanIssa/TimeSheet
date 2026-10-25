namespace TimeSheet.BLL.DTOs.Request
{
    public enum ReportGroupBy
    {
        Day = 0,
        Week = 1,  // weeks start on TimeSheetSettings.WeekStart (Sunday)
        Month = 2
    }

    // Query-string filters for the project reports.
    public class ReportFilterRequest
    {
        // Daily report: defaults to the project start date. Employees report: no lower bound.
        public DateOnly? From { get; set; }

        // Daily report: defaults to today (business time zone). Employees report: no upper bound.
        public DateOnly? To { get; set; }

        // Daily report only.
        public ReportGroupBy GroupBy { get; set; } = ReportGroupBy.Day;
    }
}
