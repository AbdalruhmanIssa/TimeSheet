namespace TimeSheet.DAL.Enums
{
    // Stored as int — values are pinned, never renumber.
    // Approved = 1 is referenced by the comment check constraint in TimeEntryReviewConfig.
    public enum ReviewDecision
    {
        Approved = 1,
        Rejected = 2,
        Reopened = 3
    }
}
