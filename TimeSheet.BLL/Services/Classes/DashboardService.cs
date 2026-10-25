using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TimeSheet.BLL.DTOs.Response;
using TimeSheet.BLL.Services.Interface;
using TimeSheet.BLL.Settings;
using TimeSheet.DAL.Enums;
using TimeSheet.DAL.Models;
using TimeSheet.DAL.Repositories.Interface;
using TimeSheet.DAL.Utilities;

namespace TimeSheet.BLL.Services.Classes
{
    public class DashboardService : IDashboardService
    {
        private readonly IUnitOfWork _uow;
        private readonly IProjectAccessService _access;
        private readonly IReportService _reports;
        private readonly IBusinessClock _clock;
        private readonly TimeSheetSettings _settings;

        public DashboardService(
            IUnitOfWork uow,
            IProjectAccessService access,
            IReportService reports,
            IBusinessClock clock,
            IOptions<TimeSheetSettings> settings)
        {
            _uow = uow;
            _access = access;
            _reports = reports;
            _clock = clock;
            _settings = settings.Value;
        }

        // ---------------------------------------------------------------- Employee

        public async Task<EmployeeDashboardResponse> GetEmployeeDashboardAsync(string employeeId)
        {
            var today = _clock.Today;
            var weekStart = _clock.StartOfWeek(today);
            var monthStart = new DateOnly(today.Year, today.Month, 1);

            var mine = _uow.Repo<TimeEntry>().Query().Where(e => e.EmployeeId == employeeId);

            // Same statuses the daily cap counts.
            var counted = mine.Where(e => e.Status == TimeEntryStatus.PendingManager
                                          || e.Status == TimeEntryStatus.PendingDirector
                                          || e.Status == TimeEntryStatus.Approved);

            var hoursToday = await counted.Where(e => e.WorkDate == today).SumAsync(e => e.Hours);

            return new EmployeeDashboardResponse
            {
                Today = today,
                WeekStart = weekStart,
                HoursToday = hoursToday,
                DailyLimit = _settings.MaxDailyHours,
                RemainingToday = Math.Max(0, _settings.MaxDailyHours - hoursToday),
                HoursThisWeek = await counted
                    .Where(e => e.WorkDate >= weekStart && e.WorkDate <= today)
                    .SumAsync(e => e.Hours),
                ApprovedHoursThisMonth = await mine
                    .Where(e => e.Status == TimeEntryStatus.Approved && e.WorkDate >= monthStart && e.WorkDate <= today)
                    .SumAsync(e => e.Hours),
                PendingCount = await mine.CountAsync(e => e.Status == TimeEntryStatus.PendingManager
                                                         || e.Status == TimeEntryStatus.PendingDirector),
                RejectedCount = await mine.CountAsync(e => e.Status == TimeEntryStatus.Rejected),
                ActiveProjectCount = await _access.VisibleProjects(employeeId, AppRoles.Employee)
                    .CountAsync(p => p.Status == ProjectStatus.Active)
            };
        }

        // ---------------------------------------------------------------- Manager / Director

        public async Task<ManagerDashboardResponse> GetManagerDashboardAsync(string managerId)
        {
            return new ManagerDashboardResponse
            {
                PendingReview = await PendingQueueAsync(managerId, AppRoles.Manager, TimeEntryStatus.PendingManager),
                ActiveProjectCount = await ActiveProjectCountAsync(managerId, AppRoles.Manager),
                BudgetAlerts = await BudgetAlertsAsync(managerId, AppRoles.Manager)
            };
        }

        public async Task<DirectorDashboardResponse> GetDirectorDashboardAsync(string directorId)
        {
            var today = _clock.Today;
            var monthStart = new DateOnly(today.Year, today.Month, 1);
            var projectIds = _access.VisibleProjects(directorId, AppRoles.Director).Select(p => p.Id);

            return new DirectorDashboardResponse
            {
                PendingReview = await PendingQueueAsync(directorId, AppRoles.Director, TimeEntryStatus.PendingDirector),
                ActiveProjectCount = await ActiveProjectCountAsync(directorId, AppRoles.Director),
                BudgetAlerts = await BudgetAlertsAsync(directorId, AppRoles.Director),
                ApprovedHoursThisMonth = await _uow.Repo<TimeEntry>().Query()
                    .Where(e => projectIds.Contains(e.ProjectId)
                                && e.Status == TimeEntryStatus.Approved
                                && e.WorkDate >= monthStart && e.WorkDate <= today)
                    .SumAsync(e => e.Hours)
            };
        }

        private async Task<PendingQueueResponse> PendingQueueAsync(string userId, string role, TimeEntryStatus waiting)
        {
            var projectIds = _access.VisibleProjects(userId, role).Select(p => p.Id);
            var queue = _uow.Repo<TimeEntry>().Query()
                .Where(e => projectIds.Contains(e.ProjectId) && e.Status == waiting);

            return new PendingQueueResponse
            {
                Count = await queue.CountAsync(),
                Hours = await queue.SumAsync(e => e.Hours),
                OldestSubmittedAt = await queue.Select(e => (DateTime?)e.SubmittedAt).MinAsync()
            };
        }

        private Task<int> ActiveProjectCountAsync(string userId, string role)
            => _access.VisibleProjects(userId, role).CountAsync(p => p.Status == ProjectStatus.Active);

        private async Task<BudgetAlertsResponse> BudgetAlertsAsync(string userId, string role)
        {
            var summaries = (await _reports.GetProjectSummariesAsync(userId, role))
                .Where(s => s.ProjectStatus != ProjectStatus.Archived)
                .ToList();

            // One bucket per project, worst first (same order as the portfolio).
            return new BudgetAlertsResponse
            {
                ExceededCount = summaries.Count(s => s.BudgetStatus == BudgetStatus.Exceeded),
                ProjectedToExceedCount = summaries.Count(s => s.BudgetStatus != BudgetStatus.Exceeded && s.IsProjectedToExceed),
                NearLimitCount = summaries.Count(s => s.BudgetStatus == BudgetStatus.NearLimit && !s.IsProjectedToExceed)
            };
        }
    }
}
