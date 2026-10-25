using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using System.Text;
using TimeSheet.BLL.DTOs.Request;
using TimeSheet.BLL.DTOs.Response;
using TimeSheet.BLL.Exceptions;
using TimeSheet.BLL.Services.Classes;
using TimeSheet.BLL.Services.Interface;
using TimeSheet.BLL.Settings;
using TimeSheet.DAL.Enums;
using TimeSheet.DAL.Models;
using TimeSheet.DAL.Repositories.Interface;
using TimeSheet.DAL.Utilities;
using static TimeSheet.Tests.TestHelpers;

namespace TimeSheet.Tests.Services
{
    public class ReportPdfServiceTests
    {
        private const string Director = "dir-1";
        private static readonly Guid ProjectId = Guid.NewGuid();

        private readonly Mock<IReportService> _reports = new();
        private readonly Mock<IUnitOfWork> _uow = new();
        private readonly ReportPdfService _sut;

        public ReportPdfServiceTests()
        {
            _uow.SetupRepo(new List<ApplicationUser> { NewUser(Director, "Dina Director") });

            var settings = Options.Create(new TimeSheetSettings
            {
                BusinessTimeZone = "Asia/Riyadh",
                WeekStart = DayOfWeek.Sunday,
                MaxDailyHours = 12,
                MaxBackdateDays = 30
            });
            var clock = new BusinessClock(new FixedTimeProvider(new DateTimeOffset(2026, 10, 26, 9, 0, 0, TimeSpan.Zero)), settings);

            _sut = new ReportPdfService(_reports.Object, _uow.Object, clock);
        }

        [Fact]
        public async Task GenerateProjectReportAsync_ProducesAPdf_NamedAfterProjectAndPeriod()
        {
            SetupReports(Daily(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)));

            var (content, fileName) = await _sut.GenerateProjectReportAsync(Director, AppRoles.Director, ProjectId,
                new ReportFilterRequest { From = new DateOnly(2026, 10, 1), To = new DateOnly(2026, 10, 31) });

            Encoding.ASCII.GetString(content, 0, 5).Should().Be("%PDF-");
            content.Length.Should().BeGreaterThan(2000);
            fileName.Should().Be("PRJ-0001_report_20261001-20261031.pdf");
        }

        [Fact]
        public async Task GenerateProjectReportAsync_EmployeeTableUsesTheDailyReportsResolvedPeriod()
        {
            // No dates sent: the daily report resolves them (project start .. today).
            SetupReports(Daily(new DateOnly(2026, 8, 26), new DateOnly(2026, 10, 26)));

            await _sut.GenerateProjectReportAsync(Director, AppRoles.Director, ProjectId, new ReportFilterRequest());

            _reports.Verify(r => r.GetEmployeeBreakdownAsync(Director, AppRoles.Director, ProjectId,
                It.Is<ReportFilterRequest>(f => f.From == new DateOnly(2026, 8, 26) && f.To == new DateOnly(2026, 10, 26))), Times.Once);
        }

        [Fact]
        public async Task GenerateProjectReportAsync_ManyRows_SpansSeveralPages()
        {
            SetupReports(Daily(new DateOnly(2025, 10, 27), new DateOnly(2026, 10, 26))); // 365 daily rows

            var (content, _) = await _sut.GenerateProjectReportAsync(Director, AppRoles.Director, ProjectId, new ReportFilterRequest());

            CountPages(content).Should().BeGreaterThan(3);
        }

        [Fact]
        public async Task GenerateProjectReportAsync_OutOfScope_ThrowsNotFound_AndBuildsNothing()
        {
            _reports.Setup(r => r.GetProjectSummaryAsync(Director, AppRoles.Director, ProjectId))
                .ThrowsAsync(new NotFoundException("Project not found."));

            var act = () => _sut.GenerateProjectReportAsync(Director, AppRoles.Director, ProjectId, new ReportFilterRequest());

            await act.Should().ThrowAsync<NotFoundException>();
            _reports.Verify(r => r.GetDailyReportAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<ReportFilterRequest>()), Times.Never);
        }

        // ------------------------------------------------------------------

        private void SetupReports(DailyReportResponse daily)
        {
            _reports.Setup(r => r.GetProjectSummaryAsync(Director, AppRoles.Director, ProjectId)).ReturnsAsync(Summary());
            _reports.Setup(r => r.GetDailyReportAsync(Director, AppRoles.Director, ProjectId, It.IsAny<ReportFilterRequest>()))
                .ReturnsAsync(daily);
            _reports.Setup(r => r.GetEmployeeBreakdownAsync(Director, AppRoles.Director, ProjectId, It.IsAny<ReportFilterRequest>()))
                .ReturnsAsync(new List<EmployeeHoursRow>
                {
                    new() { EmployeeId = "emp-1", EmployeeName = "Eve", IsActiveOnProject = true, ApprovedHours = 30, SharePercent = 75, PendingHours = 4 },
                    new() { EmployeeId = "emp-2", EmployeeName = "Carl", IsActiveOnProject = false, ApprovedHours = 10, SharePercent = 25 }
                });
        }

        private static ProjectSummaryReport Summary()
        {
            var s = new ProjectSummaryReport
            {
                ProjectId = ProjectId,
                Code = "PRJ-0001",
                Name = "Acme ERP Rollout",
                ClientName = "Acme Trading Co.",
                ManagerName = "Mona Manager",
                ProjectStatus = ProjectStatus.Active
            };
            BudgetCalculator.Apply(s, budgetHours: 100, approvedHours: 85, pendingManagerHours: 10, pendingDirectorHours: 8);
            return s;
        }

        private static DailyReportResponse Daily(DateOnly from, DateOnly to)
        {
            var report = new DailyReportResponse
            {
                ProjectId = ProjectId,
                Code = "PRJ-0001",
                Name = "Acme ERP Rollout",
                BudgetHours = 100,
                From = from,
                To = to,
                GroupBy = ReportGroupBy.Day,
                OpeningBalance = 45
            };

            var running = report.OpeningBalance;
            for (var day = from; day <= to; day = day.AddDays(1))
            {
                var hours = day.DayNumber % 7 == 0 ? 2m : 0m;
                running += hours;
                report.Rows.Add(new DailyReportRow { PeriodStart = day, PeriodEnd = day, Hours = hours, RunningTotal = running, Remaining = 100 - running });
            }

            report.PeriodHours = running - report.OpeningBalance;
            report.ClosingBalance = running;
            return report;
        }

        // Counts "/Type /Page" objects (not "/Pages") in the raw PDF.
        private static int CountPages(byte[] pdf)
        {
            var text = Encoding.Latin1.GetString(pdf);
            return System.Text.RegularExpressions.Regex.Matches(text, @"/Type\s*/Page(?!s)").Count;
        }
    }
}
