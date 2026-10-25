using FluentAssertions;
using TimeSheet.BLL.DTOs.Response;
using TimeSheet.BLL.Services.Classes;
using TimeSheet.DAL.Enums;

namespace TimeSheet.Tests.Services
{
    // DESIGN §4: < 80 % OnTrack · 80–100 % NearLimit · > 100 % Exceeded.
    public class BudgetCalculatorTests
    {
        [Theory]
        [InlineData(0, BudgetStatus.OnTrack)]
        [InlineData(79.99, BudgetStatus.OnTrack)]
        [InlineData(80, BudgetStatus.NearLimit)]
        [InlineData(100, BudgetStatus.NearLimit)]
        [InlineData(100.01, BudgetStatus.Exceeded)]
        [InlineData(150, BudgetStatus.Exceeded)]
        public void Apply_ClassifiesBudgetStatusByUsage(double approved, BudgetStatus expected)
        {
            var report = Calculate(budget: 100, approved: (decimal)approved);

            report.BudgetStatus.Should().Be(expected);
        }

        [Fact]
        public void StatusFor_UsesExactUsage_NotTheRoundedOne()
        {
            // 79.996 % rounds to 80.00 for display but is still under 80 %.
            BudgetCalculator.StatusFor(79.996m).Should().Be(BudgetStatus.OnTrack);

            var report = Calculate(budget: 300, approved: 239.99m); // 79.9966…%
            report.UsagePercent.Should().Be(80.00m);
            report.BudgetStatus.Should().Be(BudgetStatus.OnTrack);
        }

        [Fact]
        public void Apply_UnderBudgetButPendingWouldExceed_IsProjectedToExceed()
        {
            var report = Calculate(budget: 100, approved: 90, pendingManager: 8, pendingDirector: 5);

            report.ActualHours.Should().Be(90);
            report.RemainingHours.Should().Be(10);
            report.PendingManagerHours.Should().Be(8);
            report.PendingDirectorHours.Should().Be(5);
            report.ProjectedRemaining.Should().Be(-3);
            report.IsProjectedToExceed.Should().BeTrue();
            report.UsagePercent.Should().Be(90);
            report.BudgetStatus.Should().Be(BudgetStatus.NearLimit);
            report.ExceededBy.Should().Be(0);
        }

        [Fact]
        public void Apply_OverBudget_RemainingIsNegative_AndExceededByIsSet()
        {
            var report = Calculate(budget: 100, approved: 120);

            report.RemainingHours.Should().Be(-20);
            report.ExceededBy.Should().Be(20);
            report.UsagePercent.Should().Be(120);
            report.BudgetStatus.Should().Be(BudgetStatus.Exceeded);
            report.IsProjectedToExceed.Should().BeTrue();
        }

        [Fact]
        public void Apply_ExactlyOnBudget_IsNotProjectedToExceed()
        {
            var report = Calculate(budget: 100, approved: 60, pendingManager: 40);

            report.ProjectedRemaining.Should().Be(0);
            report.IsProjectedToExceed.Should().BeFalse();
        }

        private static ProjectSummaryReport Calculate(decimal budget, decimal approved,
            decimal pendingManager = 0, decimal pendingDirector = 0)
        {
            var report = new ProjectSummaryReport();
            BudgetCalculator.Apply(report, budget, approved, pendingManager, pendingDirector);
            return report;
        }
    }
}
