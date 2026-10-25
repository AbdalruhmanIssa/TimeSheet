namespace TimeSheet.DAL.Enums
{
    // Stored as int — values are pinned, never renumber.
    // Cancelled = 4 is referenced by the filtered unique index in TimeEntryConfig.
    public enum TimeEntryStatus
    {
        PendingManager = 0,
        PendingDirector = 1,
        Approved = 2,
        Rejected = 3,
        Cancelled = 4
    }
}
