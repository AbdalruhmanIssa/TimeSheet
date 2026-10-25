using Mapster;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Data;
using TimeSheet.BLL.DTOs.Request;
using TimeSheet.BLL.DTOs.Response;
using TimeSheet.BLL.Exceptions;
using TimeSheet.BLL.Services.Interface;
using TimeSheet.BLL.Settings;
using TimeSheet.DAL.Enums;
using TimeSheet.DAL.Models;
using TimeSheet.DAL.Repositories.Interface;
using TimeSheet.DAL.Utilities;

namespace TimeSheet.BLL.Services.Classes
{
    public class TimeEntryService : ITimeEntryService
    {
        // Statuses that count toward the daily cap (Rejected / Cancelled don't).
        private static readonly TimeEntryStatus[] CountedStatuses =
        {
            TimeEntryStatus.PendingManager,
            TimeEntryStatus.PendingDirector,
            TimeEntryStatus.Approved
        };

        private const string ConcurrencyMessage =
            "This entry was just changed (probably reviewed). Reload it and try again.";

        private readonly IUnitOfWork _uow;
        private readonly IProjectAccessService _access;
        private readonly IBusinessClock _clock;
        private readonly TimeSheetSettings _settings;

        public TimeEntryService(
            IUnitOfWork uow,
            IProjectAccessService access,
            IBusinessClock clock,
            IOptions<TimeSheetSettings> settings)
        {
            _uow = uow;
            _access = access;
            _clock = clock;
            _settings = settings.Value;
        }

        // ---------------------------------------------------------------- Reads

        public async Task<List<ProjectOptionResponse>> GetProjectOptionsAsync(string employeeId)
        {
            return await _access.VisibleProjects(employeeId, AppRoles.Employee)
                .Where(p => p.Status == ProjectStatus.Active)
                .OrderBy(p => p.Code)
                .ProjectToType<ProjectOptionResponse>()
                .ToListAsync();
        }

        public async Task<List<TimeEntryResponse>> GetMyEntriesAsync(string employeeId, TimeEntryFilterRequest filter)
        {
            if (filter.From is { } from && filter.To is { } to && from > to)
                throw new BadRequestException("'from' cannot be after 'to'.");

            var query = MyEntries(employeeId);

            query = filter.Status is { } status
                ? query.Where(e => e.Status == status)
                : query.Where(e => e.Status != TimeEntryStatus.Cancelled);

            if (filter.From is { } fromDate)
                query = query.Where(e => e.WorkDate >= fromDate);

            if (filter.To is { } toDate)
                query = query.Where(e => e.WorkDate <= toDate);

            if (filter.ProjectId is { } projectId)
                query = query.Where(e => e.ProjectId == projectId);

            return await query
                .OrderByDescending(e => e.WorkDate)
                .ThenBy(e => e.Project.Code)
                .ProjectToType<TimeEntryResponse>()
                .ToListAsync();
        }

        public async Task<TimeEntryResponse> GetMyEntryAsync(string employeeId, Guid entryId)
        {
            return await MyEntries(employeeId)
                       .Where(e => e.Id == entryId)
                       .ProjectToType<TimeEntryResponse>()
                       .FirstOrDefaultAsync()
                   ?? throw new NotFoundException("Time entry not found.");
        }

        public async Task<List<TimeEntryReviewResponse>> GetHistoryAsync(string employeeId, Guid entryId)
        {
            if (!await MyEntries(employeeId).AnyAsync(e => e.Id == entryId))
                throw new NotFoundException("Time entry not found.");

            return await _uow.Repo<TimeEntryReview>().Query()
                .Where(r => r.TimeEntryId == entryId)
                .OrderBy(r => r.CreatedAt)
                .ThenBy(r => r.Level)
                .ProjectToType<TimeEntryReviewResponse>()
                .ToListAsync();
        }

        // ---------------------------------------------------------------- Submit / edit / cancel

        public async Task<TimeEntryResponse> SubmitAsync(string employeeId, SubmitTimeEntryRequest request)
        {
            var projectId = request.ProjectId!.Value;
            var workDate = request.WorkDate!.Value;

            EnsureValidHours(request.Hours);
            EnsureDateInWindow(workDate);

            // Serializable: the duplicate-date and daily-cap checks read other rows,
            // so two submits at once must not both pass. (A deadlock victim is retried by EF.)
            var entryId = await _uow.ExecuteInTransactionAsync(async () =>
            {
                var project = await GetLoggableProjectAsync(employeeId, projectId);
                await EnsureNoOtherEntryAsync(employeeId, project, workDate, excludeEntryId: null);
                await EnsureDailyCapAsync(employeeId, workDate, request.Hours, excludeEntryId: null);

                var entry = new TimeEntry
                {
                    ProjectId = projectId,
                    EmployeeId = employeeId,
                    WorkDate = workDate,
                    Hours = request.Hours,
                    Notes = NullIfBlank(request.Notes),
                    Status = TimeEntryStatus.PendingManager,
                    SubmittedAt = _clock.UtcNow
                };

                await _uow.Repo<TimeEntry>().AddAsync(entry);
                await _uow.SaveChangesAsync();
                return entry.Id;
            }, IsolationLevel.Serializable);

            return await GetMyEntryAsync(employeeId, entryId);
        }

        public async Task<TimeEntryResponse> UpdateAsync(string employeeId, Guid entryId, UpdateTimeEntryRequest request)
        {
            var projectId = request.ProjectId!.Value;
            var workDate = request.WorkDate!.Value;

            EnsureValidHours(request.Hours);

            try
            {
                await _uow.ExecuteInTransactionAsync(async () =>
                {
                    var entry = await FindMyEntryAsync(employeeId, entryId);

                    if (entry.Status is not (TimeEntryStatus.PendingManager or TimeEntryStatus.Rejected))
                        throw new BadRequestException(
                            $"Only entries waiting for the manager or rejected can be edited. This entry is {entry.Status}.");

                    // The date window only applies when the date changes, so a rejected (or reopened)
                    // entry from before the window can still be fixed and resubmitted.
                    if (workDate != entry.WorkDate)
                        EnsureDateInWindow(workDate);

                    var project = await GetLoggableProjectAsync(employeeId, projectId);
                    await EnsureNoOtherEntryAsync(employeeId, project, workDate, excludeEntryId: entry.Id);
                    await EnsureDailyCapAsync(employeeId, workDate, request.Hours, excludeEntryId: entry.Id);

                    entry.ProjectId = projectId;
                    entry.WorkDate = workDate;
                    entry.Hours = request.Hours;
                    entry.Notes = NullIfBlank(request.Notes);

                    // Editing a rejected entry = resubmitting it.
                    if (entry.Status == TimeEntryStatus.Rejected)
                    {
                        entry.Status = TimeEntryStatus.PendingManager;
                        entry.SubmittedAt = _clock.UtcNow;
                    }

                    await _uow.SaveChangesAsync();
                    return true;
                }, IsolationLevel.Serializable);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new BadRequestException(ConcurrencyMessage);
            }

            return await GetMyEntryAsync(employeeId, entryId);
        }

        public async Task<TimeEntryResponse> CancelAsync(string employeeId, Guid entryId)
        {
            var entry = await FindMyEntryAsync(employeeId, entryId);

            if (entry.Status != TimeEntryStatus.PendingManager)
                throw new BadRequestException(
                    $"Only entries still waiting for the manager can be cancelled. This entry is {entry.Status}.");

            entry.Status = TimeEntryStatus.Cancelled;

            try
            {
                await _uow.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new BadRequestException(ConcurrencyMessage);
            }

            return await GetMyEntryAsync(employeeId, entryId);
        }

        // ---------------------------------------------------------------- Rules

        private IQueryable<TimeEntry> MyEntries(string employeeId)
            => _uow.Repo<TimeEntry>().Query().Where(e => e.EmployeeId == employeeId);

        // Tracked entity; someone else's entry is a 404.
        private async Task<TimeEntry> FindMyEntryAsync(string employeeId, Guid entryId)
        {
            return await MyEntries(employeeId).FirstOrDefaultAsync(e => e.Id == entryId)
                   ?? throw new NotFoundException("Time entry not found.");
        }

        // Rule 1: active assignment (404 otherwise — out of scope) and an Active project.
        private async Task<Project> GetLoggableProjectAsync(string employeeId, Guid projectId)
        {
            var project = await _access.VisibleProjects(employeeId, AppRoles.Employee)
                              .FirstOrDefaultAsync(p => p.Id == projectId)
                          ?? throw new NotFoundException("Project not found or you are not assigned to it.");

            if (project.Status != ProjectStatus.Active)
                throw new BadRequestException(
                    $"Hours can only be logged on active projects. {project.Code} is {project.Status}.");

            return project;
        }

        private void EnsureDateInWindow(DateOnly workDate)
        {
            var today = _clock.Today;

            if (workDate > today)
                throw new BadRequestException("You can't log hours for a future date.");

            var earliest = today.AddDays(-_settings.MaxBackdateDays);
            if (workDate < earliest)
                throw new BadRequestException(
                    $"You can only log hours for the last {_settings.MaxBackdateDays} days (from {earliest:yyyy-MM-dd}).");
        }

        private static void EnsureValidHours(decimal hours)
        {
            if (decimal.Round(hours, 2) != hours)
                throw new BadRequestException("Hours can have at most 2 decimal places.");
        }

        // Rule 2: one entry per project per day. A Rejected entry still holds the date;
        // only Cancelled ones free it. The unique index is the safety net.
        private async Task EnsureNoOtherEntryAsync(string employeeId, Project project, DateOnly workDate, Guid? excludeEntryId)
        {
            var existing = await MyEntries(employeeId)
                .Where(e => e.ProjectId == project.Id
                            && e.WorkDate == workDate
                            && e.Status != TimeEntryStatus.Cancelled
                            && (excludeEntryId == null || e.Id != excludeEntryId))
                .Select(e => (TimeEntryStatus?)e.Status)
                .FirstOrDefaultAsync();

            if (existing is null)
                return;

            throw new BadRequestException(existing == TimeEntryStatus.Rejected
                ? $"You already have a rejected entry for {project.Code} on {workDate:yyyy-MM-dd}. Fix and resubmit that entry instead."
                : $"You already have an entry for {project.Code} on {workDate:yyyy-MM-dd}. Edit that entry instead.");
        }

        // Rule 3: max hours per day across all projects.
        private async Task EnsureDailyCapAsync(string employeeId, DateOnly workDate, decimal hours, Guid? excludeEntryId)
        {
            var otherHours = await MyEntries(employeeId)
                .Where(e => e.WorkDate == workDate
                            && CountedStatuses.Contains(e.Status)
                            && (excludeEntryId == null || e.Id != excludeEntryId))
                .SumAsync(e => e.Hours);

            var max = _settings.MaxDailyHours;
            if (otherHours + hours > max)
                throw new BadRequestException(
                    $"Daily limit is {max:0.##} h. You already have {otherHours:0.##} h on {workDate:yyyy-MM-dd}, " +
                    $"so you can add at most {Math.Max(0, max - otherHours):0.##} h.");
        }

        private static string? NullIfBlank(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
