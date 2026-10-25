using TimeSheet.DAL.Enums;

namespace TimeSheet.BLL.DTOs.Response
{
    // DESIGN §4 — budget fields per project, all computed (actual hours are never stored).
    public class ProjectSummaryReport
    {
        public Guid ProjectId { get; set; }
        public string Code { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string ClientName { get; set; } = null!;
        public string ManagerName { get; set; } = null!;
        public ProjectStatus ProjectStatus { get; set; }

        // Fixed by contract.
        public decimal BudgetHours { get; set; }

        // SUM of Approved entries.
        public decimal ActualHours { get; set; }

        // Budget − Actual (can be negative).
        public decimal RemainingHours { get; set; }

        public decimal PendingManagerHours { get; set; }
        public decimal PendingDirectorHours { get; set; }

        // Budget − Actual − PendingManager − PendingDirector.
        public decimal ProjectedRemaining { get; set; }

        // Actual / Budget × 100 (2 decimals).
        public decimal UsagePercent { get; set; }

        public BudgetStatus BudgetStatus { get; set; }

        // ProjectedRemaining < 0.
        public bool IsProjectedToExceed { get; set; }

        // max(0, Actual − Budget).
        public decimal ExceededBy { get; set; }
    }
}
