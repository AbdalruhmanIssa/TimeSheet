using TimeSheet.BLL.DTOs.Response;
using TimeSheet.DAL.Enums;

namespace TimeSheet.BLL.Services.Classes
{
    // DESIGN §4 formulas in one place.
    public static class BudgetCalculator
    {
        public const decimal NearLimitPercent = 80m;
        public const decimal LimitPercent = 100m;

        // Fills the budget fields of `report` from the four inputs.
        public static void Apply(
            ProjectSummaryReport report,
            decimal budgetHours,
            decimal approvedHours,
            decimal pendingManagerHours,
            decimal pendingDirectorHours)
        {
            // BudgetHours has a DB check > 0, so the division is safe.
            var usage = budgetHours > 0 ? approvedHours / budgetHours * 100m : 0m;

            report.BudgetHours = budgetHours;
            report.ActualHours = approvedHours;
            report.RemainingHours = budgetHours - approvedHours;
            report.PendingManagerHours = pendingManagerHours;
            report.PendingDirectorHours = pendingDirectorHours;
            report.ProjectedRemaining = budgetHours - approvedHours - pendingManagerHours - pendingDirectorHours;
            report.UsagePercent = Math.Round(usage, 2, MidpointRounding.AwayFromZero);
            report.BudgetStatus = StatusFor(usage);
            report.IsProjectedToExceed = report.ProjectedRemaining < 0;
            report.ExceededBy = Math.Max(0, approvedHours - budgetHours);
        }

        // Classified on the exact (unrounded) usage: 79.996 % is still OnTrack.
        public static BudgetStatus StatusFor(decimal usagePercent) => usagePercent switch
        {
            < NearLimitPercent => BudgetStatus.OnTrack,
            <= LimitPercent => BudgetStatus.NearLimit,
            _ => BudgetStatus.Exceeded
        };
    }
}
