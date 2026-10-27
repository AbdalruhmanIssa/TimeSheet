using FluentAssertions;
using TimeSheet.BLL.DTOs.Request;
using TimeSheet.BLL.Exceptions;
using TimeSheet.DAL.Enums;
using TimeSheet.DAL.Utilities;

namespace TimeSheet.Tests.Integration
{
    // The whole flow through the real services on SQL Server: the rules hold end to end,
    // and every query (Mapster projections, review subqueries, grouped sums) translates to SQL.
    [Collection(SqlServerCollection.Name)]
    [Trait("Category", "Integration")]
    public class WorkflowIntegrationTests
    {
        private readonly SqlServerFixture _db;

        public WorkflowIntegrationTests(SqlServerFixture db) => _db = db;

        [Fact]
        public async Task FullLifecycle_WithBudgetLockAndReopen()
        {
            var w = await _db.CreateWorldAsync(budget: 10);
            var day = SqlServerFixture.Today.AddDays(-1);
            await using var s = _db.CreateServices();

            // Employee logs 6 h -> PendingManager.
            var entry = await s.Entries.SubmitAsync(w.EmployeeId, new SubmitTimeEntryRequest
            {
                ProjectId = w.ProjectId, WorkDate = day, Hours = 6, Notes = "Workshop"
            });
            entry.Status.Should().Be(TimeEntryStatus.PendingManager);

            // Manager rejects -> employee sees level + comment (latest review, via SQL subquery).
            await s.Approvals.RejectAsync(w.ManagerId, AppRoles.Manager, entry.Id, new ReviewRequest { Comment = "Add the task names" });
            var mine = await s.Entries.GetMyEntryAsync(w.EmployeeId, entry.Id);
            mine.Status.Should().Be(TimeEntryStatus.Rejected);
            mine.RejectedAtLevel.Should().Be(ReviewLevel.Manager);
            mine.RejectionComment.Should().Be("Add the task names");

            // Employee fixes -> resubmitted.
            mine = await s.Entries.UpdateAsync(w.EmployeeId, entry.Id, new UpdateTimeEntryRequest
            {
                ProjectId = w.ProjectId, WorkDate = day, Hours = 6, Notes = "Workshop: requirements + data mapping"
            });
            mine.Status.Should().Be(TimeEntryStatus.PendingManager);
            mine.RejectedAtLevel.Should().BeNull();

            // Manager approves -> director's queue shows it with budget impact and who approved.
            await s.Approvals.ApproveAsync(w.ManagerId, AppRoles.Manager, entry.Id);
            var pending = (await s.Approvals.GetPendingAsync(w.DirectorId, AppRoles.Director, new PagedRequest())).Items.Single();
            pending.ManagerApprovedBy.Should().Be(w.ManagerId.Split('-')[1] + " " + w.ManagerId.Split('-')[2]);
            pending.BudgetImpact!.ActualHours.Should().Be(0);
            pending.BudgetImpact.RemainingAfterApproval.Should().Be(4);

            // Budget not locked yet: no approved hours.
            (await s.Projects.GetByIdAsync(w.DirectorId, AppRoles.Director, w.ProjectId)).IsBudgetLocked.Should().BeFalse();

            // Director approves -> Approved; the budget is now locked.
            await s.Approvals.ApproveAsync(w.DirectorId, AppRoles.Director, entry.Id);
            var project = await s.Projects.GetByIdAsync(w.DirectorId, AppRoles.Director, w.ProjectId);
            project.IsBudgetLocked.Should().BeTrue();

            var changeBudget = () => s.Projects.UpdateAsync(w.DirectorId, w.ProjectId, Update(project, budget: 20));
            await changeBudget.Should().ThrowAsync<BadRequestException>().WithMessage("The budget is locked*");

            var summary = await s.Reports.GetProjectSummaryAsync(w.DirectorId, AppRoles.Director, w.ProjectId);
            summary.ActualHours.Should().Be(6);
            summary.UsagePercent.Should().Be(60);

            // Director reopens -> Rejected at Director level; hours leave Actual; budget unlocks.
            await s.Approvals.ReopenAsync(w.DirectorId, entry.Id, new ReviewRequest { Comment = "Belongs to project B" });
            mine = await s.Entries.GetMyEntryAsync(w.EmployeeId, entry.Id);
            mine.Status.Should().Be(TimeEntryStatus.Rejected);
            mine.RejectedAtLevel.Should().Be(ReviewLevel.Director);
            mine.RejectionComment.Should().Be("Belongs to project B");

            (await s.Reports.GetProjectSummaryAsync(w.DirectorId, AppRoles.Director, w.ProjectId)).ActualHours.Should().Be(0);
            project = await s.Projects.GetByIdAsync(w.DirectorId, AppRoles.Director, w.ProjectId);
            project.IsBudgetLocked.Should().BeFalse();
            (await s.Projects.UpdateAsync(w.DirectorId, w.ProjectId, Update(project, budget: 20))).BudgetHours.Should().Be(20);

            // History, oldest first.
            var history = await s.Entries.GetHistoryAsync(w.EmployeeId, entry.Id);
            history.Select(h => (h.Level, h.Decision)).Should().Equal(
                (ReviewLevel.Manager, ReviewDecision.Rejected),
                (ReviewLevel.Manager, ReviewDecision.Approved),
                (ReviewLevel.Director, ReviewDecision.Approved),
                (ReviewLevel.Director, ReviewDecision.Reopened));
        }

        [Fact]
        public async Task DailyCapAndDuplicateDay_OnSqlServer()
        {
            var w = await _db.CreateWorldAsync();
            var day = SqlServerFixture.Today.AddDays(-3);
            await using var s = _db.CreateServices();

            await s.Entries.SubmitAsync(w.EmployeeId, Submit(w.ProjectId, day, 8));

            var sameProject = () => s.Entries.SubmitAsync(w.EmployeeId, Submit(w.ProjectId, day, 1));
            await sameProject.Should().ThrowAsync<BadRequestException>().WithMessage("You already have an entry*");

            var overCap = () => s.Entries.SubmitAsync(w.EmployeeId, Submit(w.Project2Id, day, 5));
            await overCap.Should().ThrowAsync<BadRequestException>().WithMessage("Daily limit is 12 h. You already have 8 h*");

            (await s.Entries.SubmitAsync(w.EmployeeId, Submit(w.Project2Id, day, 4))).Hours.Should().Be(4);
        }

        [Fact]
        public async Task CancelledEntryFreesTheDay_OnSqlServer()
        {
            var w = await _db.CreateWorldAsync();
            var day = SqlServerFixture.Today.AddDays(-4);
            await using var s = _db.CreateServices();

            var first = await s.Entries.SubmitAsync(w.EmployeeId, Submit(w.ProjectId, day, 8));
            await s.Entries.CancelAsync(w.EmployeeId, first.Id);

            var again = await s.Entries.SubmitAsync(w.EmployeeId, Submit(w.ProjectId, day, 12));

            again.Hours.Should().Be(12);
        }

        [Fact]
        public async Task AssigningSomeoneAlreadyOnTheTeam_IsRejected_ButReAssigningAfterRemovalWorks()
        {
            var w = await _db.CreateWorldAsync();
            await using var s = _db.CreateServices();

            var again = () => s.Projects.AssignEmployeesAsync(w.DirectorId, w.ProjectId,
                new AssignEmployeesRequest { EmployeeIds = new() { w.EmployeeId } });
            await again.Should().ThrowAsync<BadRequestException>().WithMessage("Already assigned to this project:*");

            await s.Projects.RemoveEmployeeAsync(w.DirectorId, w.ProjectId, w.EmployeeId);
            var project = await again();

            project.Employees.Should().ContainSingle(e => e.EmployeeId == w.EmployeeId && e.IsActive);
        }

        [Fact]
        public async Task Paging_OnSqlServer_PagesDoNotOverlap_AndCountsAreRight()
        {
            var w = await _db.CreateWorldAsync();
            await using var s = _db.CreateServices();

            // 2 projects in this world, 1 per page.
            var first = await s.Projects.GetAllAsync(w.DirectorId, AppRoles.Director, new ProjectFilterRequest { Page = 1, PageSize = 1 });
            var second = await s.Projects.GetAllAsync(w.DirectorId, AppRoles.Director, new ProjectFilterRequest { Page = 2, PageSize = 1 });
            var third = await s.Projects.GetAllAsync(w.DirectorId, AppRoles.Director, new ProjectFilterRequest { Page = 3, PageSize = 1 });

            first.TotalCount.Should().Be(2);
            first.TotalPages.Should().Be(2);
            first.HasNextPage.Should().BeTrue();
            second.HasNextPage.Should().BeFalse();
            first.Items.Concat(second.Items).Select(p => p.Id).Should().BeEquivalentTo(new[] { w.ProjectId, w.Project2Id });
            third.Items.Should().BeEmpty();

            // Director's queue: budget impact is filled for the entries on the requested page.
            foreach (var day in new[] { 1, 2, 3 })
            {
                var entry = await s.Entries.SubmitAsync(w.EmployeeId, Submit(w.ProjectId, SqlServerFixture.Today.AddDays(-day), 2));
                await s.Approvals.ApproveAsync(w.ManagerId, AppRoles.Manager, entry.Id);
            }

            var queuePage2 = await s.Approvals.GetPendingAsync(w.DirectorId, AppRoles.Director, new PagedRequest { Page = 2, PageSize = 2 });
            queuePage2.TotalCount.Should().Be(3);
            queuePage2.Items.Should().ContainSingle().Which.BudgetImpact.Should().NotBeNull();

            var mine = await s.Entries.GetMyEntriesAsync(w.EmployeeId, new TimeEntryFilterRequest { PageSize = 2 });
            mine.TotalCount.Should().Be(3);
            mine.Items.Should().HaveCount(2);
        }

        [Fact]
        public async Task Scoping_OnSqlServer_EveryoneElseGets404()
        {
            var w = await _db.CreateWorldAsync();
            await using var s = _db.CreateServices();
            var entry = await s.Entries.SubmitAsync(w.EmployeeId, Submit(w.ProjectId, SqlServerFixture.Today, 2));

            await Must404(() => s.Projects.GetByIdAsync(w.OtherDirectorId, AppRoles.Director, w.ProjectId));
            await Must404(() => s.Projects.GetByIdAsync(w.OtherManagerId, AppRoles.Manager, w.ProjectId));
            await Must404(() => s.Approvals.ApproveAsync(w.OtherManagerId, AppRoles.Manager, entry.Id));
            await Must404(() => s.Entries.GetMyEntryAsync(w.Employee2Id, entry.Id));
            await Must404(() => s.Entries.CancelAsync(w.Employee2Id, entry.Id));
            await Must404(() => s.Reports.GetProjectSummaryAsync(w.OtherDirectorId, AppRoles.Director, w.ProjectId));
            await Must404(() => s.Reports.GetDailyReportAsync(w.OtherManagerId, AppRoles.Manager, w.ProjectId, new ReportFilterRequest()));

            (await s.Approvals.GetPendingAsync(w.OtherManagerId, AppRoles.Manager, new PagedRequest())).Items.Should().BeEmpty();
            (await s.Reports.GetPortfolioAsync(w.OtherDirectorId, new PortfolioFilterRequest())).Items.Should().BeEmpty();
        }

        [Fact]
        public async Task Reports_And_Dashboards_OnSqlServer()
        {
            var w = await _db.CreateWorldAsync(budget: 20);
            await using var s = _db.CreateServices();

            // Week of Sun 2026-10-18 .. Sat 2026-10-24, and Sun 2026-10-25 .. today (Mon 10-26).
            var e1 = await s.Entries.SubmitAsync(w.EmployeeId, Submit(w.ProjectId, new DateOnly(2026, 10, 20), 8));
            var e2 = await s.Entries.SubmitAsync(w.Employee2Id, Submit(w.ProjectId, new DateOnly(2026, 10, 25), 6));
            await s.Entries.SubmitAsync(w.EmployeeId, Submit(w.ProjectId, new DateOnly(2026, 10, 26), 9)); // stays pending
            foreach (var id in new[] { e1.Id, e2.Id })
            {
                await s.Approvals.ApproveAsync(w.ManagerId, AppRoles.Manager, id);
                await s.Approvals.ApproveAsync(w.DirectorId, AppRoles.Director, id);
            }

            // Summary: 14 approved of 20 = 70 % OnTrack; +9 pending -> projected over.
            var summary = await s.Reports.GetProjectSummaryAsync(w.ManagerId, AppRoles.Manager, w.ProjectId);
            summary.ActualHours.Should().Be(14);
            summary.PendingManagerHours.Should().Be(9);
            summary.BudgetStatus.Should().Be(BudgetStatus.OnTrack);
            summary.IsProjectedToExceed.Should().BeTrue();

            // Daily by week with an opening balance.
            var daily = await s.Reports.GetDailyReportAsync(w.DirectorId, AppRoles.Director, w.ProjectId, new ReportFilterRequest
            {
                From = new DateOnly(2026, 10, 21), To = new DateOnly(2026, 10, 26), GroupBy = ReportGroupBy.Week
            });
            daily.OpeningBalance.Should().Be(8);
            // The week of 10-21 has no approved hours in range, so only the week with work is listed.
            daily.Rows.Select(r => (r.PeriodStart, r.Hours, r.RunningTotal)).Should().Equal(
                (new DateOnly(2026, 10, 25), 6m, 14m));

            // Per employee.
            var employees = await s.Reports.GetEmployeeBreakdownAsync(w.DirectorId, AppRoles.Director, w.ProjectId, new ReportFilterRequest());
            employees.Select(e => (e.EmployeeId, e.ApprovedHours, e.PendingHours)).Should().Equal(
                (w.EmployeeId, 8m, 9m),
                (w.Employee2Id, 6m, 0m));
            employees.Sum(e => e.SharePercent).Should().BeApproximately(100m, 0.02m);

            // Portfolio: project A (projected over) comes before project B (nothing logged).
            var portfolio = (await s.Reports.GetPortfolioAsync(w.DirectorId, new PortfolioFilterRequest())).Items;
            portfolio.Select(p => p.ProjectId).Should().Equal(w.ProjectId, w.Project2Id);

            // Dashboards.
            var director = await s.Dashboards.GetDirectorDashboardAsync(w.DirectorId);
            director.PendingReview.Count.Should().Be(0);
            director.ApprovedHoursThisMonth.Should().Be(14);
            director.BudgetAlerts.ProjectedToExceedCount.Should().Be(1);

            var manager = await s.Dashboards.GetManagerDashboardAsync(w.ManagerId);
            manager.PendingReview.Count.Should().Be(1);
            manager.PendingReview.Hours.Should().Be(9);

            var employee = await s.Dashboards.GetEmployeeDashboardAsync(w.EmployeeId);
            employee.HoursToday.Should().Be(9);
            employee.RemainingToday.Should().Be(3);
            employee.PendingCount.Should().Be(1);
            employee.ApprovedHoursThisMonth.Should().Be(8);
        }

        // ------------------------------------------------------------------

        private static async Task Must404(Func<Task> act) => await act.Should().ThrowAsync<NotFoundException>();

        private static SubmitTimeEntryRequest Submit(Guid projectId, DateOnly day, decimal hours)
            => new() { ProjectId = projectId, WorkDate = day, Hours = hours, Notes = "integration test" };

        private static UpdateProjectRequest Update(BLL.DTOs.Response.ProjectDetailsResponse p, decimal budget) => new()
        {
            Name = p.Name,
            Description = p.Description,
            ClientId = p.ClientId,
            ManagerId = p.ManagerId,
            BudgetHours = budget,
            StartDate = p.StartDate,
            EndDate = p.EndDate
        };
    }
}
