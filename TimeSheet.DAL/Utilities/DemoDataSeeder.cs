using Microsoft.EntityFrameworkCore;
using TimeSheet.DAL.Data;
using TimeSheet.DAL.Enums;
using TimeSheet.DAL.Models;

namespace TimeSheet.DAL.Utilities
{
    // Demo data for Development (see Program.cs): 6 clients and 10 projects that together cover
    // every case — every project status, every budget state, every approval path.
    //
    //   PRJ-0001 Sunrise Patient Portal      Active     on track        pending (both levels), manager-rejected, cancelled, removed member
    //   PRJ-0002 Horizon Fleet Tracking      Active     EXCEEDED        approving its pending entry goes further over
    //   PRJ-0003 Cedar POS Upgrade           Active     near limit
    //   PRJ-0004 Atlas Network Monitoring    Active     projected to exceed (pending hours would push it over)
    //   PRJ-0005 Oasis Site Planning         Completed  near limit      client is inactive
    //   PRJ-0006 BlueWave Claims Portal      OnHold
    //   PRJ-0007 Cedar Loyalty App           Active     reopened, director-rejected, resubmitted after rejection, cancelled + re-logged
    //   PRJ-0008 Atlas Billing Migration     Archived   exactly on budget, team removed
    //   PRJ-0009 Sunrise Lab Integration     Active     brand new: no hours, budget not locked
    //   PRJ-0010 Horizon Warehouse Robotics  Active     6 pending entries for one manager (bulk approve)
    //
    // Everything is relative to today, so the data always looks current. Work days are Sun–Thu;
    // "workday n" = the n-th work day before today. Runs only when there are no projects yet.
    internal sealed class DemoDataSeeder
    {
        private static readonly TimeEntryStatus[] CountedStatuses =
            { TimeEntryStatus.PendingManager, TimeEntryStatus.PendingDirector, TimeEntryStatus.Approved };

        private readonly AppDbContext _db;
        private readonly IReadOnlyDictionary<string, string> _userIds; // username -> id
        private readonly DateTime _now = DateTime.UtcNow;
        private readonly DateOnly _today;
        private readonly List<DateOnly> _workdays = new();

        // Guards so the demo data itself obeys the rules it demonstrates.
        private readonly Dictionary<(string EmployeeId, DateOnly Day), decimal> _hoursPerDay = new();
        private readonly HashSet<(string EmployeeId, Guid ProjectId, DateOnly Day)> _takenDays = new();

        // today comes from the business time zone: just after midnight in Riyadh, UTC is still on
        // the previous day, and the data would end a day early.
        public DemoDataSeeder(AppDbContext db, IReadOnlyDictionary<string, string> userIds, DateOnly today)
        {
            _db = db;
            _userIds = userIds;
            _today = today;

            for (var day = _today.AddDays(-1); _workdays.Count < 200; day = day.AddDays(-1))
                if (day.DayOfWeek is not (DayOfWeek.Friday or DayOfWeek.Saturday))
                    _workdays.Add(day);
        }

        public async Task SeedAsync()
        {
            if (await _db.Projects.AnyAsync())
                return;

            // ---------------------------------------------------------------- Clients

            var sunrise = AddClient("Sunrise Medical Center", "Dr. Samir Halabi", "samir.halabi@sunrise-medical.example", "+966 11 410 2200");
            var horizon = AddClient("Horizon Logistics", "Mona Fakhoury", "mona.fakhoury@horizon-logistics.example", "+966 12 655 3010");
            var cedar = AddClient("Cedar Retail Group", "Karim Najjar", "karim.najjar@cedar-retail.example", "+966 11 293 7740");
            var atlas = AddClient("Atlas Telecom", "Hiba Saad", "hiba.saad@atlas-telecom.example", "+966 13 860 1199");
            var bluewave = AddClient("BlueWave Insurance", "Fadi Kanaan", "fadi.kanaan@bluewave-ins.example", "+966 11 478 5520");
            var oasis = AddClient("Oasis Construction", "Walid Tamimi", "walid.tamimi@oasis-build.example", "+966 13 822 4100", isActive: false);

            // ---------------------------------------------------------------- PRJ-0001: healthy, full workflow

            var p1 = AddProject("PRJ-0001", "Sunrise Patient Portal",
                "Online appointments, patient profiles and lab results for Sunrise Medical Center.",
                sunrise, "khaled", "omar", budget: 400, startWorkday: 70, endInDays: 60, ProjectStatus.Active,
                team: new[] { "ahmad", "sara", "mohammad" }, removed: new[] { "noor" },
                notes: new[] { "Appointment booking API", "Patient profile screens", "HL7 lab results mapping",
                               "Login and two-factor flow", "Code review and bug fixes", "Sprint planning and estimates" });

            Approved(p1, "ahmad", fromWorkday: 10, days: 20, hours: 6);
            Approved(p1, "sara", fromWorkday: 10, days: 20, hours: 5);
            Approved(p1, "mohammad", fromWorkday: 10, days: 10, hours: 4);
            Approved(p1, "noor", fromWorkday: 60, days: 5, hours: 6);           // before she was moved off the team
            PendingManager(p1, "ahmad", workday: 1, hours: 6);
            PendingManager(p1, "ahmad", workday: 2, hours: 6);
            PendingDirector(p1, "sara", workday: 1, hours: 5);
            RejectedByManager(p1, "mohammad", workday: 3, hours: 4, notes: "misc",
                comment: "Please describe what you worked on — \"misc\" is not enough to approve.");
            Cancelled(p1, "sara", workday: 4, hours: 5);

            // ---------------------------------------------------------------- PRJ-0002: over budget

            var p2 = AddProject("PRJ-0002", "Horizon Fleet Tracking",
                "Live GPS tracking, geofences and driver app for Horizon's delivery fleet.",
                horizon, "khaled", "lina", budget: 120, startWorkday: 30, endInDays: 20, ProjectStatus.Active,
                team: new[] { "noor", "faris" },
                notes: new[] { "GPS ingestion service", "Live map view", "Geofence alerts", "Driver app sync", "Load testing the tracking API" });

            Approved(p2, "noor", fromWorkday: 8, days: 15, hours: 5);
            Approved(p2, "faris", fromWorkday: 8, days: 10, hours: 6);           // 135 h of 120 h
            PendingDirector(p2, "faris", workday: 1, hours: 6);                  // approving it adds to the overrun
            PendingManager(p2, "noor", workday: 2, hours: 5);

            // ---------------------------------------------------------------- PRJ-0003: near the limit

            var p3 = AddProject("PRJ-0003", "Cedar POS Upgrade",
                "New point-of-sale terminals, payments and offline mode for Cedar stores.",
                cedar, "khaled", "omar", budget: 200, startWorkday: 25, endInDays: 30, ProjectStatus.Active,
                team: new[] { "dana", "bilal" },
                notes: new[] { "Payment terminal integration", "Receipt printing", "Offline sales mode", "Inventory sync", "Store rollout support" });

            Approved(p3, "dana", fromWorkday: 5, days: 15, hours: 6);
            Approved(p3, "bilal", fromWorkday: 5, days: 16, hours: 5);           // 170 h of 200 h = 85 %
            PendingManager(p3, "dana", workday: 1, hours: 4);

            // ---------------------------------------------------------------- PRJ-0004: on track now, pending would push it over

            var p4 = AddProject("PRJ-0004", "Atlas Network Monitoring",
                "Network health dashboards, alert rules and on-call paging for Atlas Telecom.",
                atlas, "khaled", "yousef", budget: 160, startWorkday: 20, endInDays: 45, ProjectStatus.Active,
                team: new[] { "maya", "zaid", "ahmad" },
                notes: new[] { "SNMP collector", "Alert rules engine", "Dashboard widgets", "Incident timeline", "On-call integration" });

            Approved(p4, "maya", fromWorkday: 6, days: 12, hours: 5);
            Approved(p4, "zaid", fromWorkday: 6, days: 10, hours: 5);            // 110 h of 160 h
            PendingManager(p4, "maya", workday: 1, hours: 8);
            PendingManager(p4, "maya", workday: 2, hours: 8);
            PendingManager(p4, "zaid", workday: 1, hours: 7);
            PendingManager(p4, "zaid", workday: 2, hours: 7);
            PendingManager(p4, "ahmad", workday: 1, hours: 6);
            PendingDirector(p4, "ahmad", workday: 3, hours: 6);
            PendingDirector(p4, "zaid", workday: 3, hours: 7);
            PendingDirector(p4, "maya", workday: 3, hours: 6);                   // +55 pending -> 5 h over

            // ---------------------------------------------------------------- PRJ-0005: completed, client no longer active

            var p5 = AddProject("PRJ-0005", "Oasis Site Planning",
                "Site surveys, cost estimates and permit tracking for Oasis Construction.",
                oasis, "khaled", "hala", budget: 80, startWorkday: 45, endInDays: null, ProjectStatus.Completed,
                team: new[] { "bilal", "reem" },
                notes: new[] { "Site survey import", "Cost estimate module", "Permit tracking", "Client handover documents" },
                endWorkday: 28);

            Approved(p5, "bilal", fromWorkday: 30, days: 8, hours: 5);
            Approved(p5, "reem", fromWorkday: 30, days: 7, hours: 5);            // 75 h of 80 h

            // ---------------------------------------------------------------- PRJ-0006: on hold

            var p6 = AddProject("PRJ-0006", "BlueWave Claims Portal",
                "Self-service claim submission and adjuster workflow — paused while the client re-scopes.",
                bluewave, "khaled", "tariq", budget: 250, startWorkday: 48, endInDays: 90, ProjectStatus.OnHold,
                team: new[] { "noor", "reem" },
                notes: new[] { "Claim submission form", "Document upload", "Adjuster workflow" });

            Approved(p6, "noor", fromWorkday: 35, days: 6, hours: 6);
            Approved(p6, "reem", fromWorkday: 40, days: 4, hours: 5);

            // ---------------------------------------------------------------- PRJ-0007: every review path

            var p7 = AddProject("PRJ-0007", "Cedar Loyalty App",
                "Points, rewards catalog and push notifications for Cedar's loyalty members.",
                cedar, "rania", "lina", budget: 300, startWorkday: 22, endInDays: 75, ProjectStatus.Active,
                team: new[] { "faris", "dana", "zaid", "maya" },
                notes: new[] { "Points engine", "Rewards catalog", "Push notifications", "Member onboarding", "UI review" });

            Approved(p7, "faris", fromWorkday: 10, days: 10, hours: 4);
            Approved(p7, "dana", fromWorkday: 8, days: 10, hours: 4);
            Approved(p7, "zaid", fromWorkday: 8, days: 8, hours: 4);
            Approved(p7, "maya", fromWorkday: 12, days: 8, hours: 4);
            Reopened(p7, "faris", workday: 4, hours: 5, notes: "Driver app sync fixes",
                comment: "These hours belong to Horizon Fleet Tracking (PRJ-0002) — please move them.");
            RejectedByDirector(p7, "dana", workday: 3, hours: 4, notes: "UI review",
                comment: "Four hours for a UI review is too much — split it per screen and resubmit.");
            PendingDirector(p7, "dana", workday: 2, hours: 4);
            Resubmitted(p7, "zaid", workday: 5, hours: 4, notes: "Points engine: expiry rules (TKT-412, TKT-415)",
                rejectionComment: "Add the ticket numbers so I can match this to the sprint.");
            Cancelled(p7, "maya", workday: 4, hours: 3);
            PendingManager(p7, "maya", workday: 4, hours: 3);                    // same day, after cancelling

            // ---------------------------------------------------------------- PRJ-0008: archived

            var p8 = AddProject("PRJ-0008", "Atlas Billing Migration",
                "Moved Atlas billing from the legacy system — finished and archived.",
                atlas, "rania", "yousef", budget: 180, startWorkday: 135, endInDays: null, ProjectStatus.Archived,
                team: Array.Empty<string>(), removed: new[] { "mohammad", "sara" },
                notes: new[] { "Legacy data export", "Invoice reconciliation", "Cut-over rehearsal", "Post-migration fixes" },
                endWorkday: 100);

            Approved(p8, "mohammad", fromWorkday: 110, days: 20, hours: 5);
            Approved(p8, "sara", fromWorkday: 110, days: 16, hours: 5);          // exactly 180 h

            // ---------------------------------------------------------------- PRJ-0009: brand new

            AddProject("PRJ-0009", "Sunrise Lab Integration",
                "Connect Sunrise's lab analyzers to the patient portal.",
                sunrise, "rania", "hala", budget: 100, startWorkday: 2, endInDays: 60, ProjectStatus.Active,
                team: new[] { "sara", "noor" },
                notes: new[] { "Analyzer interface spec" });

            // ---------------------------------------------------------------- PRJ-0010: a manager queue to bulk-approve

            var p10 = AddProject("PRJ-0010", "Horizon Warehouse Robotics",
                "Task scheduling and pick-path optimisation for Horizon's warehouse robots.",
                horizon, "rania", "tariq", budget: 150, startWorkday: 20, endInDays: 100, ProjectStatus.Active,
                team: new[] { "bilal", "reem", "zaid" },
                notes: new[] { "Robot task scheduler", "Pick-path optimisation", "Safety zone configuration", "Fleet status board" });

            Approved(p10, "bilal", fromWorkday: 8, days: 5, hours: 4);
            Approved(p10, "reem", fromWorkday: 8, days: 8, hours: 6);
            PendingManager(p10, "bilal", workday: 1, hours: 4);
            PendingManager(p10, "bilal", workday: 2, hours: 4);
            PendingManager(p10, "reem", workday: 1, hours: 6);
            PendingManager(p10, "reem", workday: 2, hours: 6);
            PendingManager(p10, "reem", workday: 3, hours: 6);
            PendingManager(p10, "zaid", workday: 4, hours: 3);

            await _db.SaveChangesAsync();
        }

        // ---------------------------------------------------------------- Entry shapes

        private void Approved(Project p, string employee, int fromWorkday, int days, decimal hours)
        {
            for (var w = fromWorkday; w < fromWorkday + days; w++)
                AddEntry(p, employee, w, hours, null, TimeEntryStatus.Approved,
                    (ReviewLevel.Manager, ReviewDecision.Approved, null),
                    (ReviewLevel.Director, ReviewDecision.Approved, null));
        }

        private void PendingManager(Project p, string employee, int workday, decimal hours)
            => AddEntry(p, employee, workday, hours, null, TimeEntryStatus.PendingManager);

        private void PendingDirector(Project p, string employee, int workday, decimal hours)
            => AddEntry(p, employee, workday, hours, null, TimeEntryStatus.PendingDirector,
                (ReviewLevel.Manager, ReviewDecision.Approved, null));

        private void RejectedByManager(Project p, string employee, int workday, decimal hours, string notes, string comment)
            => AddEntry(p, employee, workday, hours, notes, TimeEntryStatus.Rejected,
                (ReviewLevel.Manager, ReviewDecision.Rejected, comment));

        private void RejectedByDirector(Project p, string employee, int workday, decimal hours, string notes, string comment)
            => AddEntry(p, employee, workday, hours, notes, TimeEntryStatus.Rejected,
                (ReviewLevel.Manager, ReviewDecision.Approved, null),
                (ReviewLevel.Director, ReviewDecision.Rejected, comment));

        private void Reopened(Project p, string employee, int workday, decimal hours, string notes, string comment)
            => AddEntry(p, employee, workday, hours, notes, TimeEntryStatus.Rejected,
                (ReviewLevel.Manager, ReviewDecision.Approved, null),
                (ReviewLevel.Director, ReviewDecision.Approved, null),
                (ReviewLevel.Director, ReviewDecision.Reopened, comment));

        // Rejected by the manager, then fixed by the employee: back to PendingManager, history kept.
        private void Resubmitted(Project p, string employee, int workday, decimal hours, string notes, string rejectionComment)
        {
            var entry = AddEntry(p, employee, workday, hours, notes, TimeEntryStatus.PendingManager,
                (ReviewLevel.Manager, ReviewDecision.Rejected, rejectionComment));

            var resubmittedAt = Clamp(entry.WorkDate.ToDateTime(new TimeOnly(11, 0)).AddDays(1), order: 4);
            entry.SubmittedAt = resubmittedAt;
            entry.UpdatedAt = resubmittedAt;
        }

        private void Cancelled(Project p, string employee, int workday, decimal hours)
        {
            var entry = AddEntry(p, employee, workday, hours, null, TimeEntryStatus.Cancelled);
            entry.UpdatedAt = Clamp(entry.SubmittedAt.AddHours(2), order: 1);
        }

        private TimeEntry AddEntry(Project p, string employee, int workday, decimal hours, string? notes,
            TimeEntryStatus status, params (ReviewLevel Level, ReviewDecision Decision, string? Comment)[] reviews)
        {
            var employeeId = _userIds[employee];
            var day = _workdays[workday - 1];

            if (status != TimeEntryStatus.Cancelled && !_takenDays.Add((employeeId, p.Id, day)))
                throw new InvalidOperationException($"Demo data: {employee} already has an entry on {p.Code} for {day}.");

            if (CountedStatuses.Contains(status))
            {
                var total = _hoursPerDay.GetValueOrDefault((employeeId, day)) + hours;
                if (total > 12)
                    throw new InvalidOperationException($"Demo data: {employee} would have {total} h on {day} (cap is 12).");
                _hoursPerDay[(employeeId, day)] = total;
            }

            // Logged at the end of the work day; manager reviews next morning, director the morning after.
            var submittedAt = Clamp(day.ToDateTime(new TimeOnly(15, 0)), order: 0);

            var entry = new TimeEntry
            {
                Project = p,
                EmployeeId = employeeId,
                WorkDate = day,
                Hours = hours,
                Notes = notes ?? NoteFor(p, day),
                Status = status,
                SubmittedAt = submittedAt,
                CreatedAt = submittedAt
            };

            for (var i = 0; i < reviews.Length; i++)
            {
                var (level, decision, comment) = reviews[i];
                entry.Reviews.Add(new TimeEntryReview
                {
                    ReviewerId = level == ReviewLevel.Manager ? p.ManagerId : p.DirectorId,
                    Level = level,
                    Decision = decision,
                    Comment = comment,
                    CreatedAt = Clamp(day.ToDateTime(new TimeOnly(7 + i, 30)).AddDays(i + 1), order: i + 1)
                });
            }

            _db.TimeEntries.Add(entry);
            return entry;
        }

        // ---------------------------------------------------------------- Clients / projects

        private Client AddClient(string name, string contact, string email, string phone, bool isActive = true)
        {
            var client = new Client
            {
                Name = name,
                ContactName = contact,
                ContactEmail = email,
                Phone = phone,
                IsActive = isActive,
                CreatedAt = _workdays[^1].ToDateTime(new TimeOnly(9, 0))
            };
            _db.Clients.Add(client);
            return client;
        }

        private readonly Dictionary<Guid, string[]> _notes = new();

        private Project AddProject(string code, string name, string description, Client client,
            string director, string manager, decimal budget, int startWorkday, int? endInDays, ProjectStatus status,
            string[] team, string[] notes, string[]? removed = null, int? endWorkday = null)
        {
            var start = _workdays[startWorkday - 1];
            var project = new Project
            {
                Id = Guid.NewGuid(),
                Code = code,
                Name = name,
                Description = description,
                Client = client,
                DirectorId = _userIds[director],
                ManagerId = _userIds[manager],
                BudgetHours = budget,
                StartDate = start,
                EndDate = endWorkday is { } w ? _workdays[w - 1] : endInDays is { } d ? _today.AddDays(d) : null,
                Status = status,
                CreatedAt = start.ToDateTime(new TimeOnly(8, 0))
            };

            foreach (var e in team)
                project.Assignments.Add(new ProjectAssignment
                {
                    EmployeeId = _userIds[e],
                    IsActive = true,
                    CreatedAt = project.CreatedAt
                });

            foreach (var e in removed ?? Array.Empty<string>())
                project.Assignments.Add(new ProjectAssignment
                {
                    EmployeeId = _userIds[e],
                    IsActive = false,
                    CreatedAt = project.CreatedAt,
                    UpdatedAt = Clamp(project.CreatedAt.AddDays(30), order: 0)
                });

            _notes[project.Id] = notes;
            _db.Projects.Add(project);
            return project;
        }

        private string NoteFor(Project p, DateOnly day)
        {
            var notes = _notes[p.Id];
            return notes[day.DayNumber % notes.Length];
        }

        // Keeps every timestamp in the past and in order (submit < review 1 < review 2 < ...).
        private DateTime Clamp(DateTime at, int order)
        {
            var latest = _now.AddMinutes(-30 + order);
            return at <= latest ? at : latest;
        }
    }
}
