namespace TimeSheet.DAL.Enums
{
    // Computed only (never stored): from UsagePercent = Actual / Budget × 100.
    //   < 80 % OnTrack · 80–100 % NearLimit · > 100 % Exceeded
    public enum BudgetStatus
    {
        OnTrack = 0,
        NearLimit = 1,
        Exceeded = 2
    }
}
