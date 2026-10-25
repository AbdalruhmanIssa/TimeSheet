using Mapster;
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
    public class ApprovalService : IApprovalService
    {
        private const string AlreadyReviewedMessage = "This entry was already reviewed.";

        private readonly IUnitOfWork _uow;
        private readonly IProjectAccessService _access;

        public ApprovalService(IUnitOfWork uow, IProjectAccessService access)
        {
            _uow = uow;
            _access = access;
        }

        // ---------------------------------------------------------------- Pending lists

        public async Task<List<PendingReviewResponse>> GetPendingAsync(string reviewerId, string role)
        {
            var level = LevelOf(role);
            var waitingStatus = level == ReviewLevel.Manager
                ? TimeEntryStatus.PendingManager
                : TimeEntryStatus.PendingDirector;

            // Oldest submission first (a queue).
            var pending = await ScopedEntries(reviewerId, role)
                .Where(e => e.Status == waitingStatus)
                .OrderBy(e => e.SubmittedAt)
                .ThenBy(e => e.WorkDate)
                .ProjectToType<PendingReviewResponse>()
                .ToListAsync();

            if (level == ReviewLevel.Director && pending.Count > 0)
                await AddBudgetImpactAsync(pending);

            return pending;
        }

        // ---------------------------------------------------------------- Actions

        public async Task<PendingReviewResponse> ApproveAsync(string reviewerId, string role, Guid entryId)
        {
            await ReviewAsync(reviewerId, role, entryId, ReviewDecision.Approved, comment: null);
            return await GetScopedEntryAsync(reviewerId, role, entryId);
        }

        public async Task<PendingReviewResponse> RejectAsync(string reviewerId, string role, Guid entryId, ReviewRequest request)
        {
            var comment = RequireComment(request.Comment, "Rejecting");
            await ReviewAsync(reviewerId, role, entryId, ReviewDecision.Rejected, comment);
            return await GetScopedEntryAsync(reviewerId, role, entryId);
        }

        public async Task<PendingReviewResponse> ReopenAsync(string directorId, Guid entryId, ReviewRequest request)
        {
            var comment = RequireComment(request.Comment, "Reopening");
            await ReviewAsync(directorId, AppRoles.Director, entryId, ReviewDecision.Reopened, comment);
            return await GetScopedEntryAsync(directorId, AppRoles.Director, entryId);
        }

        public async Task<BulkReviewResponse> BulkApproveAsync(string reviewerId, string role, BulkReviewRequest request)
        {
            LevelOf(role); // fail fast on a bad role
            var result = new BulkReviewResponse();

            // Each entry on its own (own transaction); one failure doesn't stop the rest.
            foreach (var entryId in request.Ids.Distinct())
            {
                try
                {
                    await ReviewAsync(reviewerId, role, entryId, ReviewDecision.Approved, comment: null);
                    result.Approved.Add(entryId);
                }
                catch (ApiException ex)
                {
                    // Drop whatever the failed item left tracked so the next save is clean.
                    _uow.ClearChanges();
                    result.Failed.Add(new BulkReviewFailure { Id = entryId, Reason = ex.Message });
                }
            }

            return result;
        }

        // ---------------------------------------------------------------- Core

        // The one state machine:
        //   Manager  approve : PendingManager  -> PendingDirector
        //   Manager  reject  : PendingManager  -> Rejected
        //   Director approve : PendingDirector -> Approved
        //   Director reject  : PendingDirector -> Rejected
        //   Director reopen  : Approved        -> Rejected
        private async Task ReviewAsync(string reviewerId, string role, Guid entryId, ReviewDecision decision, string? comment)
        {
            var level = LevelOf(role);

            var (from, to) = (level, decision) switch
            {
                (ReviewLevel.Manager, ReviewDecision.Approved) => (TimeEntryStatus.PendingManager, TimeEntryStatus.PendingDirector),
                (ReviewLevel.Manager, ReviewDecision.Rejected) => (TimeEntryStatus.PendingManager, TimeEntryStatus.Rejected),
                (ReviewLevel.Director, ReviewDecision.Approved) => (TimeEntryStatus.PendingDirector, TimeEntryStatus.Approved),
                (ReviewLevel.Director, ReviewDecision.Rejected) => (TimeEntryStatus.PendingDirector, TimeEntryStatus.Rejected),
                (ReviewLevel.Director, ReviewDecision.Reopened) => (TimeEntryStatus.Approved, TimeEntryStatus.Rejected),
                _ => throw new ForbiddenException("Only directors can reopen entries.")
            };

            try
            {
                await _uow.ExecuteInTransactionAsync(async () =>
                {
                    // Out of scope (not your project) -> 404, as if it didn't exist.
                    var entry = await ScopedEntries(reviewerId, role).FirstOrDefaultAsync(e => e.Id == entryId)
                                ?? throw new NotFoundException("Time entry not found.");

                    if (entry.Status != from)
                        throw new BadRequestException(WrongStatusMessage(decision, level, entry.Status));

                    entry.Status = to;

                    await _uow.Repo<TimeEntryReview>().AddAsync(new TimeEntryReview
                    {
                        TimeEntryId = entry.Id,
                        ReviewerId = reviewerId,
                        Level = level,
                        Decision = decision,
                        Comment = comment
                    });

                    // RowVersion: if someone changed the entry since we read it, this throws.
                    await _uow.SaveChangesAsync();
                    return true;
                });
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new BadRequestException(AlreadyReviewedMessage);
            }
        }

        // Entries on projects the reviewer can see (one visibility rule: ProjectAccessService).
        private IQueryable<TimeEntry> ScopedEntries(string reviewerId, string role)
        {
            var projectIds = _access.VisibleProjects(reviewerId, role).Select(p => p.Id);
            return _uow.Repo<TimeEntry>().Query().Where(e => projectIds.Contains(e.ProjectId));
        }

        private async Task<PendingReviewResponse> GetScopedEntryAsync(string reviewerId, string role, Guid entryId)
        {
            return await ScopedEntries(reviewerId, role)
                       .Where(e => e.Id == entryId)
                       .ProjectToType<PendingReviewResponse>()
                       .FirstOrDefaultAsync()
                   ?? throw new NotFoundException("Time entry not found.");
        }

        private async Task AddBudgetImpactAsync(List<PendingReviewResponse> pending)
        {
            var projectIds = pending.Select(p => p.ProjectId).Distinct().ToList();

            var budgets = await _uow.Repo<Project>().Query()
                .Where(p => projectIds.Contains(p.Id))
                .Select(p => new { p.Id, p.BudgetHours })
                .ToDictionaryAsync(p => p.Id, p => p.BudgetHours);

            // Actual hours are never stored — always SUM of Approved entries.
            var actuals = await _uow.Repo<TimeEntry>().Query()
                .Where(e => projectIds.Contains(e.ProjectId) && e.Status == TimeEntryStatus.Approved)
                .GroupBy(e => e.ProjectId)
                .Select(g => new { ProjectId = g.Key, Hours = g.Sum(e => e.Hours) })
                .ToDictionaryAsync(x => x.ProjectId, x => x.Hours);

            foreach (var entry in pending)
            {
                var budget = budgets[entry.ProjectId];
                var actual = actuals.GetValueOrDefault(entry.ProjectId);
                var after = budget - actual - entry.Hours;

                entry.BudgetImpact = new BudgetImpactResponse
                {
                    BudgetHours = budget,
                    ActualHours = actual,
                    RemainingHours = budget - actual,
                    RemainingAfterApproval = after,
                    WouldExceedBudget = after < 0,
                    ExceedsBudgetBy = Math.Max(0, -after)
                };
            }
        }

        // ---------------------------------------------------------------- Helpers

        private static ReviewLevel LevelOf(string role) => role switch
        {
            AppRoles.Manager => ReviewLevel.Manager,
            AppRoles.Director => ReviewLevel.Director,
            _ => throw new ForbiddenException("Only managers and directors can review time entries.")
        };

        private static string RequireComment(string? comment, string action)
        {
            if (string.IsNullOrWhiteSpace(comment))
                throw new BadRequestException($"{action} an entry requires a comment.");
            return comment.Trim();
        }

        private static string WrongStatusMessage(ReviewDecision decision, ReviewLevel level, TimeEntryStatus status)
        {
            if (decision == ReviewDecision.Reopened)
                return $"Only approved entries can be reopened. This entry is {status}.";

            var waitingFor = level == ReviewLevel.Manager ? "manager" : "director";

            // Already moved past this level (or withdrawn) -> it was handled already.
            return status is TimeEntryStatus.Cancelled
                ? "This entry was cancelled by the employee."
                : $"This entry is not waiting for {waitingFor} review. It is {status}.";
        }
    }
}
