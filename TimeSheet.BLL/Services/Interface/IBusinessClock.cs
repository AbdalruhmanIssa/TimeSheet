namespace TimeSheet.BLL.Services.Interface
{
    // "Now" and "today" for business rules. Today is computed in
    // TimeSheetSettings.BusinessTimeZone, never from server UTC.
    public interface IBusinessClock
    {
        DateTime UtcNow { get; }

        // Wall-clock time in the business time zone (for display, e.g. "generated at").
        DateTime LocalNow { get; }

        DateOnly Today { get; }

        // First day of the week containing `date` (TimeSheetSettings.WeekStart).
        DateOnly StartOfWeek(DateOnly date);
    }
}
