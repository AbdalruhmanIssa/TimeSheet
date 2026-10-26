using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using TimeSheet.BLL.DTOs.Request;
using TimeSheet.BLL.DTOs.Response;
using TimeSheet.DAL.Enums;

namespace TimeSheet.BLL.Documents
{
    // Everything the project PDF shows, gathered by ReportPdfService.
    public class ProjectReportData
    {
        public ProjectSummaryReport Summary { get; set; } = null!;
        public DailyReportResponse Daily { get; set; } = null!;
        public List<EmployeeHoursRow> Employees { get; set; } = new();

        // Business time zone.
        public DateTime GeneratedAt { get; set; }
        public string GeneratedBy { get; set; } = null!;
    }

    // QuestPDF layout: header, budget summary (all time), hours over the period, per-employee table.
    public class ProjectReportDocument : IDocument
    {
        private const string Primary = "#1F4E79";
        private const string Muted = "#6B7280";
        private const string Border = "#D1D5DB";
        private const string HeaderFill = "#EEF2F7";
        private const string Text = "#111827";
        private const string Bad = "#B91C1C";
        private const string Warn = "#C2410C";
        private const string Good = "#15803D";

        private readonly ProjectReportData _data;

        public ProjectReportDocument(ProjectReportData data) => _data = data;

        public DocumentMetadata GetMetadata() => new()
        {
            Title = $"{_data.Summary.Code} \u2013 {_data.Summary.Name} report",
            Author = "TimeSheet",
            CreationDate = _data.GeneratedAt
        };

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(32);
                page.DefaultTextStyle(x => x.FontSize(9.5f));

                page.Header().Element(ComposeHeader);
                page.Content().PaddingTop(12).Element(ComposeContent);
                page.Footer().AlignCenter().Text(t =>
                {
                    t.DefaultTextStyle(x => x.FontColor(Muted).FontSize(8));
                    t.Span("Page ");
                    t.CurrentPageNumber();
                    t.Span(" of ");
                    t.TotalPages();
                });
            });
        }

        // ---------------------------------------------------------------- Header

        private void ComposeHeader(IContainer container)
        {
            var s = _data.Summary;
            var d = _data.Daily;

            container.Column(col =>
            {
                col.Item().Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text($"{s.Code} \u00B7 {s.Name}").FontSize(16).Bold().FontColor(Primary);
                        c.Item().Text($"Client: {s.ClientName}   \u00B7   Manager: {s.ManagerName}   \u00B7   Status: {Words(s.ProjectStatus)}")
                            .FontColor(Muted);
                    });
                    row.ConstantItem(150).AlignRight().Column(c =>
                    {
                        c.Item().AlignRight().Text("Project report").Bold();
                        c.Item().AlignRight().Text($"{d.From:dd MMM yyyy} \u2013 {d.To:dd MMM yyyy}").FontColor(Muted);
                        c.Item().AlignRight().Text($"Generated {_data.GeneratedAt:dd MMM yyyy HH:mm}").FontSize(8).FontColor(Muted);
                        c.Item().AlignRight().Text($"by {_data.GeneratedBy}").FontSize(8).FontColor(Muted);
                    });
                });
                col.Item().PaddingTop(8).LineHorizontal(1).LineColor(Primary);
            });
        }

        // ---------------------------------------------------------------- Content

        private void ComposeContent(IContainer container)
        {
            container.Column(col =>
            {
                col.Spacing(16);
                col.Item().Element(ComposeBudget);
                col.Item().Element(ComposePeriod);
                col.Item().Element(ComposeEmployees);
            });
        }

        private void ComposeBudget(IContainer container)
        {
            var s = _data.Summary;

            container.Column(col =>
            {
                col.Item().Element(c => SectionTitle(c, "Budget (all time)"));

                col.Item().PaddingTop(6).Row(row =>
                {
                    row.Spacing(8);
                    Tile(row.RelativeItem(), "Budget", Hours(s.BudgetHours));
                    Tile(row.RelativeItem(), "Actual (approved)", Hours(s.ActualHours));
                    Tile(row.RelativeItem(), "Remaining", Hours(s.RemainingHours), s.RemainingHours < 0 ? Bad : null);
                    Tile(row.RelativeItem(), "Usage", $"{s.UsagePercent:0.##} %", StatusColor(s.BudgetStatus));
                });

                col.Item().PaddingTop(8).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(3);
                        c.RelativeColumn(2);
                    });

                    KeyValue(table, "Budget status", Words(s.BudgetStatus), StatusColor(s.BudgetStatus));
                    KeyValue(table, "Pending manager review", Hours(s.PendingManagerHours));
                    KeyValue(table, "Pending director review", Hours(s.PendingDirectorHours));
                    KeyValue(table, "Projected remaining (if all pending is approved)", Hours(s.ProjectedRemaining),
                        s.IsProjectedToExceed ? Bad : null);
                    KeyValue(table, "Exceeded by", Hours(s.ExceededBy), s.ExceededBy > 0 ? Bad : null);
                });
            });
        }

        private void ComposePeriod(IContainer container)
        {
            var d = _data.Daily;
            var unit = d.GroupBy switch
            {
                ReportGroupBy.Week => "Week",
                ReportGroupBy.Month => "Month",
                _ => "Day"
            };

            container.Column(col =>
            {
                col.Item().Element(c => SectionTitle(c, $"Approved hours by {unit.ToLowerInvariant()}"));
                col.Item().PaddingTop(4).Text(
                        $"Opening balance (approved before {d.From:dd MMM yyyy}): {Hours(d.OpeningBalance)}   \u00B7   " +
                        $"In period: {Hours(d.PeriodHours)}   \u00B7   Closing balance: {Hours(d.ClosingBalance)}")
                    .FontColor(Muted);

                // Rows only exist for periods with approved hours.
                if (d.Rows.Count == 0)
                {
                    col.Item().PaddingTop(6).Text("No approved hours in this period.").FontColor(Muted);
                    return;
                }

                col.Item().PaddingTop(6).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(3);
                        c.RelativeColumn(2);
                        c.RelativeColumn(2);
                        c.RelativeColumn(2);
                    });

                    table.Header(h =>
                    {
                        HeaderCell(h.Cell(), unit);
                        HeaderCell(h.Cell(), "Hours", right: true);
                        HeaderCell(h.Cell(), "Running total", right: true);
                        HeaderCell(h.Cell(), "Remaining", right: true);
                    });

                    foreach (var r in d.Rows)
                    {
                        var period = r.PeriodStart == r.PeriodEnd
                            ? $"{r.PeriodStart:ddd dd MMM yyyy}"
                            : $"{r.PeriodStart:dd MMM} \u2013 {r.PeriodEnd:dd MMM yyyy}";

                        BodyCell(table.Cell(), period, muted: r.Hours == 0);
                        BodyCell(table.Cell(), Hours(r.Hours), right: true, muted: r.Hours == 0);
                        BodyCell(table.Cell(), Hours(r.RunningTotal), right: true);
                        BodyCell(table.Cell(), Hours(r.Remaining), right: true,
                            color: r.Remaining < 0 ? Bad : null);
                    }
                });
            });
        }

        private void ComposeEmployees(IContainer container)
        {
            var d = _data.Daily;

            container.Column(col =>
            {
                col.Item().Element(c => SectionTitle(c, "Hours per employee"));
                col.Item().PaddingTop(4).Text($"{d.From:dd MMM yyyy} \u2013 {d.To:dd MMM yyyy}").FontColor(Muted);

                if (_data.Employees.Count == 0)
                {
                    col.Item().PaddingTop(6).Text("No employees on this project.").FontColor(Muted);
                    return;
                }

                col.Item().PaddingTop(6).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(4);
                        c.RelativeColumn(2);
                        c.RelativeColumn(2);
                        c.RelativeColumn(2);
                    });

                    table.Header(h =>
                    {
                        HeaderCell(h.Cell(), "Employee");
                        HeaderCell(h.Cell(), "Approved", right: true);
                        HeaderCell(h.Cell(), "Share", right: true);
                        HeaderCell(h.Cell(), "Pending", right: true);
                    });

                    foreach (var e in _data.Employees)
                    {
                        BodyCell(table.Cell(), e.IsActiveOnProject ? e.EmployeeName : $"{e.EmployeeName} (removed)",
                            muted: !e.IsActiveOnProject);
                        BodyCell(table.Cell(), Hours(e.ApprovedHours), right: true);
                        BodyCell(table.Cell(), $"{e.SharePercent:0.##} %", right: true);
                        BodyCell(table.Cell(), Hours(e.PendingHours), right: true, muted: e.PendingHours == 0);
                    }
                });
            });
        }

        // ---------------------------------------------------------------- Building blocks

        private static void SectionTitle(IContainer container, string title)
            => container.Text(title).FontSize(12).Bold().FontColor(Primary);

        private static void Tile(IContainer container, string label, string value, string? valueColor = null)
        {
            container.Border(1).BorderColor(Border).Padding(8).Column(c =>
            {
                c.Item().Text(label).FontSize(8).FontColor(Muted);
                c.Item().Text(value).FontSize(13).Bold().FontColor(valueColor ?? Text);
            });
        }

        private static void KeyValue(TableDescriptor table, string key, string value, string? valueColor = null)
        {
            table.Cell().BorderBottom(0.5f).BorderColor(Border).PaddingVertical(3).Text(key);
            table.Cell().BorderBottom(0.5f).BorderColor(Border).PaddingVertical(3).AlignRight()
                .Text(value).SemiBold().FontColor(valueColor ?? Text);
        }

        private static void HeaderCell(IContainer cell, string text, bool right = false)
        {
            var c = cell.Background(HeaderFill).BorderBottom(1).BorderColor(Border).PaddingVertical(4).PaddingHorizontal(4);
            (right ? c.AlignRight() : c).Text(text).SemiBold();
        }

        private static void BodyCell(IContainer cell, string text, bool right = false, bool muted = false, string? color = null)
        {
            var c = cell.BorderBottom(0.5f).BorderColor(Border).PaddingVertical(3).PaddingHorizontal(4);
            (right ? c.AlignRight() : c).Text(text).FontColor(color ?? (muted ? Muted : Text));
        }

        private static string Hours(decimal hours) => $"{hours:#,0.##} h";

        private static string StatusColor(BudgetStatus status) => status switch
        {
            BudgetStatus.Exceeded => Bad,
            BudgetStatus.NearLimit => Warn,
            _ => Good
        };

        private static string Words(BudgetStatus status) => status switch
        {
            BudgetStatus.OnTrack => "On track",
            BudgetStatus.NearLimit => "Near limit",
            _ => status.ToString()
        };

        private static string Words(ProjectStatus status) => status switch
        {
            ProjectStatus.OnHold => "On hold",
            _ => status.ToString()
        };
    }
}
