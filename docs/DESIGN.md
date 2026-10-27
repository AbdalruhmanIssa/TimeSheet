# TimeSheet API — Design

ASP.NET Core (.NET 10) Web API for logging and approving project hours.
Structure and conventions follow the Brainova repo (github.com/AbdalruhmanIssa/brainova, `develop` branch).

---

## 1. Roles

| Role | Does |
|---|---|
| `Admin` | Creates user accounts and assigns each a role |
| `Director` | Manages clients; creates projects (budget, manager, employees); final approval; can reopen approved entries; sees reports |
| `Manager` | First approval on his projects; sees reports for his projects |
| `Employee` | Logs hours on projects he is assigned to |

- One role per user (Identity roles, `"Role"` claim in JWT, user id in `"Id"` claim).
- Only `Employee` users can be assigned to projects → managers/directors never log hours, so self-approval cannot happen.

---

## 2. Database

All tables except users inherit `BaseEntity` (`Guid Id`, `CreatedAt`, `UpdatedAt`, set in `SaveChanges`).

### ApplicationUser : IdentityUser
`FullName`, `IsBlocked`. (Add a 1-1 `EmployeeProfiles` table later only if HR fields are needed.)

### Clients (the employer)
| Column | Rule |
|---|---|
| `Name` | nvarchar(200), required, unique |
| `ContactName`, `ContactEmail`, `Phone` | optional |
| `IsActive` | soft-disable instead of delete |

### Projects
| Column | Rule |
|---|---|
| `Code` | unique, auto-generated (`PRJ-0007`) |
| `Name` | nvarchar(200), required, **unique** (across all directors and statuses; case-insensitive) |
| `Description` | |
| `ClientId` | FK → Clients, Restrict |
| `DirectorId` | FK → Users, Restrict |
| `ManagerId` | FK → Users, Restrict, exactly one manager |
| `BudgetHours` | decimal(9,2), check `> 0`. **Locked once the project has its first Approved entry** (contract with client; a new deal = new project) |
| `StartDate`, `EndDate` | `DateOnly`, end nullable |
| `Status` | `ProjectStatus` |
| Check constraint | `ManagerId <> DirectorId` |

Actual hours are **never stored** — always computed as SUM of Approved entries.

### ProjectAssignments
| Column | Rule |
|---|---|
| `ProjectId` | FK → Projects, Cascade |
| `EmployeeId` | FK → Users, Restrict; user must have `Employee` role |
| `IsActive` | removing an employee sets false; history stays |

Unique index `(ProjectId, EmployeeId)`.
Assigning an employee who is already active on the team is **rejected** (the whole request, naming who); a removed employee can be assigned again (same row is re-activated).

### TimeEntries
| Column | Rule |
|---|---|
| `ProjectId` | FK → Projects, Restrict |
| `EmployeeId` | FK → Users, Restrict |
| `WorkDate` | `DateOnly`; not in the future (business time zone); not older than `MaxBackdateDays` |
| `Hours` | decimal(5,2), check `> 0 AND <= 12` |
| `Notes` | nvarchar(1000); multiple tasks go in one entry's notes |
| `Status` | `TimeEntryStatus` |
| `SubmittedAt` | updated on resubmission |
| `RowVersion` | rowversion, concurrency token |

Indexes:
- `(ProjectId, Status, WorkDate)` — reports
- `(EmployeeId, WorkDate)` — my entries + daily cap check
- **Unique** `(EmployeeId, ProjectId, WorkDate) WHERE Status <> 4` (Cancelled) — one entry per employee per project per day. A Rejected entry still holds the date; the employee fixes it instead of creating a new one.

### TimeEntryReviews (history)
| Column | Rule |
|---|---|
| `TimeEntryId` | FK → TimeEntries, Cascade |
| `ReviewerId` | FK → Users, Restrict |
| `Level` | `ReviewLevel` |
| `Decision` | `ReviewDecision` |
| `Comment` | nvarchar(1000); required for Rejected and Reopened |

### Enums (store as int, values pinned)
- `TimeEntryStatus`: `PendingManager = 0, PendingDirector = 1, Approved = 2, Rejected = 3, Cancelled = 4`
- `ReviewLevel`: `Manager = 1, Director = 2`
- `ReviewDecision`: `Approved = 1, Rejected = 2, Reopened = 3`
- `ProjectStatus`: `Active = 0, OnHold = 1, Completed = 2, Archived = 3`
- `BudgetStatus` (computed only): `OnTrack, NearLimit, Exceeded`

---

## 3. Workflow

```
Employee submits
      │
      ▼
PendingManager ── manager rejects ───▶ Rejected ◀── director reopens ──┐
      │                                   │                           │
      │ manager approves                  │ employee edits            │
      ▼                                   │ & resubmits               │
PendingDirector ── director rejects ─▶ Rejected                       │
      │                                   │                           │
      │ director approves                 ▼                           │
      ▼                             PendingManager                    │
   Approved ──────────────────────────────────────────────────────────┘

Cancelled: employee withdraws while still PendingManager
```

### Rules (enforced in BLL, thrown as ApiException)
1. Employee can log only where he has an **active assignment** and the project is **Active**.
2. **One entry per project per date** — checked in code with a clear message; the unique index is the safety net.
3. **Max 12 h per day** across all of the employee's projects (`MaxDailyHours`). Counts PendingManager, PendingDirector, Approved; not Rejected/Cancelled.
4. **Edit** allowed while `PendingManager` or `Rejected`. Editing a Rejected entry → back to `PendingManager`.
5. **Cancel** only while `PendingManager`.
6. **Manager** sees/acts on `PendingManager` entries where `Project.ManagerId == me`.
7. **Director** sees/acts on `PendingDirector` entries where `Project.DirectorId == me`.
8. **Reopen** (director only): `Approved` → `Rejected`, comment required, review row with `Decision = Reopened`. The hours drop out of Actual immediately.
9. **Reject** requires a comment. Response DTO exposes `rejectedAtLevel` + comment (from latest review row).
10. **Bulk approve**: each entry processed on its own; response lists approved and failed (with reason) per id.
11. **Scoping**: one visibility rule in `ProjectAccessService`. Out-of-scope project/entry → **404**.
12. Submit and every review run inside `ExecuteInTransactionAsync`; `RowVersion` conflict → "This entry was already reviewed".

---

## 4. Budget (per project, computed)

| Field | Formula |
|---|---|
| `BudgetHours` | fixed by contract |
| `ActualHours` | SUM Approved |
| `RemainingHours` | Budget − Actual (can be negative) |
| `PendingManagerHours` | SUM PendingManager |
| `PendingDirectorHours` | SUM PendingDirector |
| `ProjectedRemaining` | Budget − Actual − PendingManager − PendingDirector |
| `UsagePercent` | Actual / Budget × 100 |
| `BudgetStatus` | < 80% OnTrack · 80–100% NearLimit · > 100% Exceeded |
| `IsProjectedToExceed` | ProjectedRemaining < 0 |
| `ExceededBy` | max(0, Actual − Budget) |

Director's pending list shows each entry's budget impact ("approving this puts the project X h over").

---

## 5. Reports (Director + Manager, scoped to own projects)

Filters: `from`, `to`, `groupBy = day | week | month` (week starts Sunday).

1. **Project summary** — the budget fields above.
2. **Daily report** — opening balance row (approved before `from`), then per day: hours, running total, remaining. Only days (weeks / months) with approved hours are listed; days nobody worked are left out.
3. **Per-employee breakdown** — hours and share per employee.
4. **Portfolio** (director) — all his projects, Exceeded / projected-to-exceed first.
5. **PDF export** — QuestPDF.

---

## 6. Solution structure

```
TimeSheet.slnx
├── TimeSheet.DAL
│   ├── Enums/            ProjectStatus, TimeEntryStatus, ReviewLevel, ReviewDecision
│   ├── Models/           BaseEntity, ApplicationUser, Client, Project,
│   │                     ProjectAssignment, TimeEntry, TimeEntryReview
│   ├── Data/
│   │   ├── AppDbContext.cs
│   │   ├── Configs/      ClientConfig, ProjectConfig, ProjectAssignmentConfig,
│   │   │                 TimeEntryConfig, TimeEntryReviewConfig
│   │   └── Migrations/
│   ├── Repositories/     IGenericRepository, IUnitOfWork (+ isolation-level overload)
│   └── Utilities/        SeedData (roles + demo users)
├── TimeSheet.BLL
│   ├── DTOs/Request/     CreateClientRequest, CreateProjectRequest, AssignEmployeesRequest,
│   │                     SubmitTimeEntryRequest, UpdateTimeEntryRequest,
│   │                     ReviewRequest, BulkReviewRequest, ReportFilterRequest
│   ├── DTOs/Response/    ProjectResponse, ProjectOptionResponse, TimeEntryResponse,
│   │                     PendingReviewResponse, BulkReviewResponse, ProjectSummaryReport,
│   │                     DailyReportResponse, EmployeeHoursRow, DashboardResponse
│   ├── Services/Interface + Classes/
│   │                     AuthenticationService, UserService, ClientService, ProjectService,
│   │                     ProjectAccessService, TimeEntryService, ApprovalService,
│   │                     ReportService, ReportPdfService, DashboardService
│   ├── Settings/         TimeSheetSettings
│   ├── Exceptions/       ApiException family + ApiExceptionMiddleware
│   └── Mapping/          MapsterConfig
├── TimeSheet.PL
│   ├── Controllers/Identity/  AuthsController
│   ├── Controllers/Admin/     UsersController
│   ├── Controllers/Director/  ClientsController, ProjectsController, TimeEntriesController,
│   │                          ReportsController, DashboardsController
│   ├── Controllers/Manager/   ProjectsController, TimeEntriesController,
│   │                          ReportsController, DashboardsController
│   ├── Controllers/Employee/  ProjectsController, TimeEntriesController, DashboardsController
│   ├── Extensions/            ClaimsPrincipalExtensions (User.GetUserId())
│   ├── Middlewares/
│   └── Program.cs
└── TimeSheet.Tests
    └── Services/         TimeEntryServiceTests, ApprovalServiceTests, ReportServiceTests
```

`appsettings.json`:
```json
"TimeSheetSettings": {
  "BusinessTimeZone": "Asia/Riyadh",
  "WeekStart": "Sunday",
  "MaxDailyHours": 12,
  "MaxBackdateDays": 30
}
```

No SignalR. Dashboards expose pending counts; the frontend polls.

---

## 7. API

Routes: `api/[area]/[controller]`, `[Authorize(Roles = "...")]`.

**Paging:** every list endpoint (users, clients, projects, my entries, pending queues, portfolio) takes
`?page=1&pageSize=20` (pageSize 1–100) and returns `{ items, page, pageSize, totalCount, totalPages, hasPreviousPage, hasNextPage }`.
Dropdowns and reports are not paged.

**Employee**
- `GET    Employee/Projects` — dropdown (active assigned projects: id, code, name)
- `POST   Employee/TimeEntries` — `{ projectId, workDate, hours, notes }`
- `GET    Employee/TimeEntries?status=&from=&to=`
- `PUT    Employee/TimeEntries/{id}` — edit; resubmits if Rejected
- `DELETE Employee/TimeEntries/{id}` — cancel (PendingManager only)
- `GET    Employee/TimeEntries/{id}/history`
- `GET    Employee/Dashboards`

**Manager**
- `GET  Manager/Projects`
- `GET  Manager/TimeEntries/pending`
- `POST Manager/TimeEntries/{id}/approve` · `POST Manager/TimeEntries/{id}/reject` `{ comment }`
- `POST Manager/TimeEntries/bulk-approve` `{ ids: [] }`
- `GET  Manager/Reports/projects/{id}/summary | daily | employees | pdf`
- `GET  Manager/Dashboards`

**Director**
- `CRUD Director/Clients`
- `POST/GET/PUT Director/Projects` (PUT refuses budget change once approved entries exist)
- `PATCH  Director/Projects/{id}/status`
- `POST   Director/Projects/{id}/employees` · `DELETE Director/Projects/{id}/employees/{employeeId}`
- `GET    Director/TimeEntries/pending` (with budget impact)
- `POST   Director/TimeEntries/{id}/approve | reject | reopen` · `POST Director/TimeEntries/bulk-approve`
- `GET    Director/Reports/portfolio` · `Director/Reports/projects/{id}/summary | daily | employees | pdf`
- `GET    Director/Dashboards`

**Admin**
- `CRUD Admin/Users`, assign role, block/unblock

---

## 8. Build order (one Claude Code session each)

1. **Scaffold** — 4 projects; copy BaseEntity, UnitOfWork, GenericRepository, exception middleware, JWT/Identity/Scalar setup from Brainova; seed roles; TimeSheetSettings; `User.GetUserId()`.
2. **Users & clients** — Admin users, Director clients.
3. **Projects & assignments** — Employee-only assignment, Manager ≠ Director, budget lock.
4. **Time entries** — dropdown, submit, edit, cancel, duplicate-date check, 12 h cap, date rules.
5. **Approval** — manager/director approve & reject, review history, RowVersion, bulk approve, reopen.
6. **Reports** — summary with pending split, daily with opening balance, per-employee, portfolio.
7. **Dashboards & PDF export.**
8. **Tests** — every allowed/blocked transition, 12 h cap, duplicate date, budget lock, scoping.
