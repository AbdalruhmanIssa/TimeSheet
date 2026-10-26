using TimeSheet.BLL.DTOs.Request;

namespace TimeSheet.BLL.DTOs.Response
{
    // Approved hours over time: an opening balance, then one row per day / week / month.
    public class DailyReportResponse
    {
        public Guid ProjectId { get; set; }
        public string Code { get; set; } = null!;
        public string Name { get; set; } = null!;
        public decimal BudgetHours { get; set; }

        public DateOnly From { get; set; }
        public DateOnly To { get; set; }
        public ReportGroupBy GroupBy { get; set; }

        // Approved hours before `From`.
        public decimal OpeningBalance { get; set; }

        // Approved hours inside From..To.
        public decimal PeriodHours { get; set; }

        // OpeningBalance + PeriodHours.
        public decimal ClosingBalance { get; set; }

        // Only the periods that have approved hours (days nobody worked are left out).
        public List<DailyReportRow> Rows { get; set; } = new();
    }

    public class DailyReportRow
    {
        // Clamped to From..To (a week/month can start before From or end after To).
        public DateOnly PeriodStart { get; set; }
        public DateOnly PeriodEnd { get; set; }

        // Approved hours in this period.
        public decimal Hours { get; set; }

        // Opening balance + all hours up to the end of this period.
        public decimal RunningTotal { get; set; }

        // Budget − RunningTotal (can be negative).
        public decimal Remaining { get; set; }
    }
}
