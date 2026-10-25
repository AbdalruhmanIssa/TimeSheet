namespace TimeSheet.BLL.DTOs.Response
{
    // "Approving this puts the project X h over" — computed against the project's
    // current Approved hours, for this entry alone.
    public class BudgetImpactResponse
    {
        public decimal BudgetHours { get; set; }

        // SUM of Approved entries right now.
        public decimal ActualHours { get; set; }

        // Budget − Actual (can be negative).
        public decimal RemainingHours { get; set; }

        // Budget − Actual − this entry's hours (can be negative).
        public decimal RemainingAfterApproval { get; set; }

        public bool WouldExceedBudget { get; set; }

        // max(0, Actual + this entry − Budget)
        public decimal ExceedsBudgetBy { get; set; }
    }
}
