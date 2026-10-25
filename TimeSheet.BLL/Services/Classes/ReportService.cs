using Microsoft.EntityFrameworkCore;
using TimeSheet.BLL.DTOs.Request;
using TimeSheet.BLL.DTOs.Response;
using TimeSheet.BLL.Exceptions;
using TimeSheet.BLL.Services.Interface;
using TimeSheet.DAL.Enums;
using TimeSheet.DAL.Models;
using TimeSheet.DAL.Repositories.Interface;
using TimeSheet.DAL.Utilities;

namespace TimeSheet.BLL.Services.Classes
{
    public class ReportService : IReportService
    {
        // Longest range allowed with groupBy=day (one row per day).
        public const int MaxDailyRows = 366;

        private readonly IUnitOfWork _uow;
        private readonly IProjectAccessService _access;
        private readonly IBusinessClock _clock;

        public ReportService(IUnitOfWork uow, IProjectAccessService access, IBusinessClock clock)
        {
            _uow = uow;
            _access = access;
            _clock = clock;
        }

        // ---------------------------------------------------------------- Summary / portfolio

        public async Task<ProjectSummaryReport> GetProjectSummaryAsync(string userId, string role, Guid projectId)
        {
            var summaries = await BuildSummariesAsync(_access.VisibleProjects(userId, role).Where(p => p.Id == projectId));
            return summaries.SingleOrDefault() ?? throw new NotFoundException("Project not found.");
        }

        public async Task<List<ProjectSummaryReport>> GetPortfolioAsync(string directorId, ProjectStatus? status)
        {
            var projects = _access.VisibleProjects(directorId, AppRoles.Director);
            if (status is { } s)
                projects = projects.Where(p => p.Status == s);

            var summaries = await BuildSummariesAsync(projects);

            // Problems first: already over budget, then heading over, then close to the limit.
            return summaries
                .OrderBy(r => r.BudgetStatus == BudgetStatus.Exceeded ? 0
                            : r.IsProjectedToExceed ? 1
                            : r.BudgetStatus == BudgetStatus.NearLimit ? 2
                            : 3)
                .ThenByDescending(r => r.UsagePercent)
                .ThenBy(r => r.Code)
                .ToList();
        }

        public Task<List<ProjectSummaryReport>> GetProjectSummariesAsync(string userId, string role)
            => BuildSummariesAsync(_access.VisibleProjects(userId, role));

        private async Task<List<ProjectSummaryReport>> BuildSummariesAsync(IQueryable<Project> projects)
        {
            var rows = await projects
                .Select(p => new
                {
                    p.Id,
                    p.Code,
                    p.Name,
                    ClientName = p.Client.Name,
                    ManagerName = p.Manager.FullName,
                    p.Status,
                    p.BudgetHours
                })
                .ToListAsync();

            if (rows.Count == 0)
                return new();

            var projectIds = rows.Select(r => r.Id).ToList();

            // One grouped query for all projects: hours per (project, status).
            var totals = await _uow.Repo<TimeEntry>().Query()
                .Where(e => projectIds.Contains(e.ProjectId)
                            && (e.Status == TimeEntryStatus.Approved
                                || e.Status == TimeEntryStatus.PendingManager
                                || e.Status == TimeEntryStatus.PendingDirector))
                .GroupBy(e => new { e.ProjectId, e.Status })
                .Select(g => new { g.Key.ProjectId, g.Key.Status, Hours = g.Sum(e => e.Hours) })
                .ToListAsync();

            decimal Hours(Guid projectId, TimeEntryStatus status)
                => totals.Where(t => t.ProjectId == projectId && t.Status == status).Sum(t => t.Hours);

            return rows.Select(r =>
            {
                var report = new ProjectSummaryReport
                {
                    ProjectId = r.Id,
                    Code = r.Code,
                    Name = r.Name,
                    ClientName = r.ClientName,
                    ManagerName = r.ManagerName,
                    ProjectStatus = r.Status
                };

                BudgetCalculator.Apply(report,
                    budgetHours: r.BudgetHours,
                    approvedHours: Hours(r.Id, TimeEntryStatus.Approved),
                    pendingManagerHours: Hours(r.Id, TimeEntryStatus.PendingManager),
                    pendingDirectorHours: Hours(r.Id, TimeEntryStatus.PendingDirector));

                return report;
            }).ToList();
        }

        // ---------------------------------------------------------------- Daily

        public async Task<DailyReportResponse> GetDailyReportAsync(string userId, string role, Guid projectId, ReportFilterRequest filter)
        {
            var project = await _access.VisibleProjects(userId, role)
                              .Where(p => p.Id == projectId)
                              .Select(p => new { p.Id, p.Code, p.Name, p.BudgetHours, p.StartDate })
                              .FirstOrDefaultAsync()
                          ?? throw new NotFoundException("Project not found.");

            var from = filter.From ?? project.StartDate;
            var to = filter.To ?? Max(_clock.Today, from);

            if (from > to)
                throw new BadRequestException("'from' cannot be after 'to'.");

            if (filter.GroupBy == ReportGroupBy.Day && to.DayNumber - from.DayNumber + 1 > MaxDailyRows)
                throw new BadRequestException(
                    $"With groupBy=day the range can be at most {MaxDailyRows} days. Use groupBy=week or month for longer ranges.");

            var approved = _uow.Repo<TimeEntry>().Query()
                .Where(e => e.ProjectId == projectId && e.Status == TimeEntryStatus.Approved);

            var opening = await approved
                .Where(e => e.WorkDate < from)
                .SumAsync(e => e.Hours);

            var hoursPerDay = await approved
                .Where(e => e.WorkDate >= from && e.WorkDate <= to)
                .GroupBy(e => e.WorkDate)
                .Select(g => new { Day = g.Key, Hours = g.Sum(e => e.Hours) })
                .ToDictionaryAsync(x => x.Day, x => x.Hours);

            var report = new DailyReportResponse
            {
                ProjectId = project.Id,
                Code = project.Code,
                Name = project.Name,
                BudgetHours = project.BudgetHours,
                From = from,
                To = to,
                GroupBy = filter.GroupBy,
                OpeningBalance = opening
            };

            var running = opening;
            foreach (var (start, end) in Periods(from, to, filter.GroupBy))
            {
                var hours = hoursPerDay.Where(d => d.Key >= start && d.Key <= end).Sum(d => d.Value);
                running += hours;

                report.Rows.Add(new DailyReportRow
                {
                    PeriodStart = start,
                    PeriodEnd = end,
                    Hours = hours,
                    RunningTotal = running,
                    Remaining = project.BudgetHours - running
                });
            }

            report.PeriodHours = running - opening;
            report.ClosingBalance = running;
            return report;
        }

        // Consecutive periods covering from..to, each clamped to the range.
        private IEnumerable<(DateOnly Start, DateOnly End)> Periods(DateOnly from, DateOnly to, ReportGroupBy groupBy)
        {
            var cursor = groupBy switch
            {
                ReportGroupBy.Week => _clock.StartOfWeek(from),
                ReportGroupBy.Month => new DateOnly(from.Year, from.Month, 1),
                _ => from
            };

            while (cursor <= to)
            {
                var next = groupBy switch
                {
                    ReportGroupBy.Week => cursor.AddDays(7),
                    ReportGroupBy.Month => cursor.AddMonths(1),
                    _ => cursor.AddDays(1)
                };

                yield return (Max(cursor, from), Min(next.AddDays(-1), to));
                cursor = next;
            }
        }

        // ---------------------------------------------------------------- Per employee

        public async Task<List<EmployeeHoursRow>> GetEmployeeBreakdownAsync(string userId, string role, Guid projectId, ReportFilterRequest filter)
        {
            if (!await _access.VisibleProjects(userId, role).AnyAsync(p => p.Id == projectId))
                throw new NotFoundException("Project not found.");

            if (filter.From is { } f && filter.To is { } t && f > t)
                throw new BadRequestException("'from' cannot be after 'to'.");

            var entries = _uow.Repo<TimeEntry>().Query()
                .Where(e => e.ProjectId == projectId
                            && (e.Status == TimeEntryStatus.Approved
                                || e.Status == TimeEntryStatus.PendingManager
                                || e.Status == TimeEntryStatus.PendingDirector));

            if (filter.From is { } from)
                entries = entries.Where(e => e.WorkDate >= from);
            if (filter.To is { } to)
                entries = entries.Where(e => e.WorkDate <= to);

            var totals = (await entries
                    .GroupBy(e => new { e.EmployeeId, e.Status })
                    .Select(g => new { g.Key.EmployeeId, g.Key.Status, Hours = g.Sum(e => e.Hours) })
                    .ToListAsync())
                .Select(x => new { x.EmployeeId, IsApproved = x.Status == TimeEntryStatus.Approved, x.Hours })
                .ToList();

            // Everyone on the team (even with 0 h) + anyone else who logged hours in the range.
            var team = await _uow.Repo<ProjectAssignment>().Query()
                .Where(a => a.ProjectId == projectId)
                .Select(a => new { a.EmployeeId, a.IsActive })
                .ToListAsync();

            var employeeIds = team.Where(a => a.IsActive).Select(a => a.EmployeeId)
                .Union(totals.Select(x => x.EmployeeId))
                .Distinct()
                .ToList();

            var names = await _uow.Repo<ApplicationUser>().Query()
                .Where(u => employeeIds.Contains(u.Id))
                .Select(u => new { u.Id, u.FullName })
                .ToDictionaryAsync(u => u.Id, u => u.FullName);

            var totalApproved = totals.Where(x => x.IsApproved).Sum(x => x.Hours);

            return employeeIds
                .Select(id =>
                {
                    var approvedHours = totals.Where(x => x.EmployeeId == id && x.IsApproved).Sum(x => x.Hours);
                    return new EmployeeHoursRow
                    {
                        EmployeeId = id,
                        EmployeeName = names.GetValueOrDefault(id, id),
                        IsActiveOnProject = team.Any(a => a.EmployeeId == id && a.IsActive),
                        ApprovedHours = approvedHours,
                        SharePercent = totalApproved > 0
                            ? Math.Round(approvedHours / totalApproved * 100m, 2, MidpointRounding.AwayFromZero)
                            : 0m,
                        PendingHours = totals.Where(x => x.EmployeeId == id && !x.IsApproved).Sum(x => x.Hours)
                    };
                })
                .OrderByDescending(r => r.ApprovedHours)
                .ThenByDescending(r => r.PendingHours)
                .ThenBy(r => r.EmployeeName)
                .ToList();
        }

        // ------------------------------------------------------------------

        private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;
        private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;
    }
}
