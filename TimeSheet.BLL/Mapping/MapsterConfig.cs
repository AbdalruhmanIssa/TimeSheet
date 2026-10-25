using Mapster;
using TimeSheet.BLL.DTOs.Response;
using TimeSheet.DAL.Enums;
using TimeSheet.DAL.Models;

namespace TimeSheet.BLL.Mapping
{
    public class MapsterConfig : IRegister
    {
        public void Register(TypeAdapterConfig config)
        {
            // ClientName <- Client.Name is picked up by Mapster's flattening convention.
            config.NewConfig<Project, ProjectResponse>()
                .Map(d => d.DirectorName, s => s.Director.FullName)
                .Map(d => d.ManagerName, s => s.Manager.FullName)
                .Map(d => d.ActiveEmployeeCount, s => s.Assignments.Count(a => a.IsActive));

            config.NewConfig<Project, ProjectDetailsResponse>()
                .Inherits<Project, ProjectResponse>()
                .Map(d => d.Employees, s => s.Assignments)
                .Ignore(d => d.IsBudgetLocked); // computed in ProjectService

            config.NewConfig<ProjectAssignment, ProjectEmployeeResponse>()
                .Map(d => d.FullName, s => s.Employee.FullName)
                .Map(d => d.UserName, s => s.Employee.UserName)
                .Map(d => d.AssignedAt, s => s.CreatedAt);

            // ProjectCode / ProjectName <- Project.Code / Project.Name by flattening.
            // Rejection info comes from the latest review row, only while the entry is Rejected.
            config.NewConfig<TimeEntry, TimeEntryResponse>()
                .Map(d => d.RejectedAtLevel, s => s.Status == TimeEntryStatus.Rejected
                    ? s.Reviews
                        .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Level)
                        .Select(r => (ReviewLevel?)r.Level)
                        .FirstOrDefault()
                    : null)
                .Map(d => d.RejectionComment, s => s.Status == TimeEntryStatus.Rejected
                    ? s.Reviews
                        .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Level)
                        .Select(r => r.Comment)
                        .FirstOrDefault()
                    : null);

            config.NewConfig<TimeEntry, PendingReviewResponse>()
                .Map(d => d.EmployeeName, s => s.Employee.FullName)
                .Map(d => d.ManagerApprovedBy, s => s.Reviews
                    .Where(r => r.Level == ReviewLevel.Manager && r.Decision == ReviewDecision.Approved)
                    .OrderByDescending(r => r.CreatedAt)
                    .Select(r => r.Reviewer.FullName)
                    .FirstOrDefault())
                .Map(d => d.ManagerApprovedAt, s => s.Reviews
                    .Where(r => r.Level == ReviewLevel.Manager && r.Decision == ReviewDecision.Approved)
                    .OrderByDescending(r => r.CreatedAt)
                    .Select(r => (DateTime?)r.CreatedAt)
                    .FirstOrDefault())
                .Ignore(d => d.BudgetImpact); // computed in ApprovalService (director only)

            config.NewConfig<TimeEntryReview, TimeEntryReviewResponse>()
                .Map(d => d.ReviewerName, s => s.Reviewer.FullName)
                .Map(d => d.ReviewedAt, s => s.CreatedAt);
        }
    }
}
