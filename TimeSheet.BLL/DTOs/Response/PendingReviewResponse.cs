using TimeSheet.DAL.Enums;

namespace TimeSheet.BLL.DTOs.Response
{
    // An entry as a reviewer sees it (pending lists and the result of approve/reject/reopen).
    public class PendingReviewResponse
    {
        public Guid Id { get; set; }

        public Guid ProjectId { get; set; }
        public string ProjectCode { get; set; } = null!;
        public string ProjectName { get; set; } = null!;

        public string EmployeeId { get; set; } = null!;
        public string EmployeeName { get; set; } = null!;

        public DateOnly WorkDate { get; set; }
        public decimal Hours { get; set; }
        public string? Notes { get; set; }

        public TimeEntryStatus Status { get; set; }
        public DateTime SubmittedAt { get; set; }

        // Who approved it at manager level (director's list).
        public string? ManagerApprovedBy { get; set; }
        public DateTime? ManagerApprovedAt { get; set; }

        // Director's pending list only: what approving this entry does to the project budget.
        public BudgetImpactResponse? BudgetImpact { get; set; }
    }
}
