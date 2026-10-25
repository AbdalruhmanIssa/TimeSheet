namespace TimeSheet.BLL.DTOs.Response
{
    // Dashboards are cheap counts the frontend polls (no SignalR).

    public class EmployeeDashboardResponse
    {
        // Business-time-zone "today" and the current week (starts on TimeSheetSettings.WeekStart).
        public DateOnly Today { get; set; }
        public DateOnly WeekStart { get; set; }

        // Pending + Approved hours logged for today, and how many more fit under the daily cap.
        public decimal HoursToday { get; set; }
        public decimal DailyLimit { get; set; }
        public decimal RemainingToday { get; set; }

        // Pending + Approved hours this week (week start .. today).
        public decimal HoursThisWeek { get; set; }

        // Approved hours from the 1st of this month.
        public decimal ApprovedHoursThisMonth { get; set; }

        // Waiting for manager or director.
        public int PendingCount { get; set; }

        // Rejected entries to fix and resubmit — "needs your attention".
        public int RejectedCount { get; set; }

        // Projects I can log hours on right now.
        public int ActiveProjectCount { get; set; }
    }

    public class ManagerDashboardResponse
    {
        // PendingManager entries on my projects.
        public PendingQueueResponse PendingReview { get; set; } = new();

        public int ActiveProjectCount { get; set; }

        // Budget warnings over my non-archived projects.
        public BudgetAlertsResponse BudgetAlerts { get; set; } = new();
    }

    public class DirectorDashboardResponse
    {
        // PendingDirector entries on my projects.
        public PendingQueueResponse PendingReview { get; set; } = new();

        public int ActiveProjectCount { get; set; }

        // Budget warnings over my non-archived projects.
        public BudgetAlertsResponse BudgetAlerts { get; set; } = new();

        // Approved hours on my projects from the 1st of this month.
        public decimal ApprovedHoursThisMonth { get; set; }
    }

    public class PendingQueueResponse
    {
        public int Count { get; set; }
        public decimal Hours { get; set; }

        // Oldest waiting submission (null when the queue is empty).
        public DateTime? OldestSubmittedAt { get; set; }
    }

    // Each project is counted in one bucket only (worst first) — same order as the portfolio.
    public class BudgetAlertsResponse
    {
        public int ExceededCount { get; set; }
        public int ProjectedToExceedCount { get; set; }
        public int NearLimitCount { get; set; }
    }
}
