namespace TimeSheet.BLL.Settings
{
    // Bound from the "TimeSheetSettings" section of appsettings.json and validated
    // at startup (Program.cs). No defaults here on purpose: values come from config.
    public class TimeSheetSettings
    {
        public const string SectionName = "TimeSheetSettings";

        // IANA or Windows id, e.g. "Asia/Riyadh". "Today" is computed in this zone.
        public string BusinessTimeZone { get; set; } = null!;

        // First day of the week for groupBy=week reports.
        public DayOfWeek WeekStart { get; set; }

        // Cap on an employee's hours per day across all projects.
        public decimal MaxDailyHours { get; set; }

        // How many days back an employee may log hours.
        public int MaxBackdateDays { get; set; }
    }
}
