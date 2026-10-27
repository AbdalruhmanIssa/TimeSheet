# TimeSheet API — demo script

A ~20-minute walkthrough in Postman that shows every feature on the seeded demo data.
The story: a week in a consulting company — employees log hours, managers approve, directors approve
and watch the budgets.

---

## Before the interview

1. **Reset the database the same morning.** The seed is relative to the day it runs, so "yesterday's"
   entries really are yesterday's. Stop the API first, then:
   ```bash
   dotnet ef database drop --force --project TimeSheet.DAL --startup-project TimeSheet.PL
   ```
   ```bash
   dotnet run --project TimeSheet.PL --launch-profile http
   ```
2. **Postman:** import both files from `docs/postman/`, select the **TimeSheet Local** environment
   (top right — without it Login can't save the token), and zoom in (Ctrl +) for screen sharing.
3. Keep a **notepad** open for ids (projects, entries, employees) — routes use `{projectId}`-style placeholders.
4. Have `http://localhost:5180/scalar/v1` open in a browser tab, in case they ask for Swagger.
5. Do one full dry run, then reset again (step 1).

---

## Accounts

Usernames are first names. One password per role.

| Role | Users | Password |
|---|---|---|
| Director | `khaled` (Khaled Mansour), `rania` (Rania Saleh) | `Director@1234` |
| Manager | `omar`, `lina`, `yousef`, `hala`, `tariq` | `Manager@1234` |
| Employee | `ahmad`, `sara`, `mohammad`, `noor`, `faris`, `dana`, `bilal`, `maya`, `zaid`, `reem` | `Employee@1234` |
| Admin | `admin` (Nadia Farouk) | `Admin@1234` |

Switch user by changing the body of **Auth → Login** — the token is saved automatically.

## The demo projects

| Project | Director / Manager | What it shows |
|---|---|---|
| PRJ-0001 Sunrise Patient Portal | Khaled / Omar | Healthy (72.5 %); pending at both levels, a manager rejection, a cancelled entry, a removed team member (Noor) |
| PRJ-0002 Horizon Fleet Tracking | Khaled / Lina | **Exceeded** (135 of 120 h); approving its pending entry goes further over |
| PRJ-0003 Cedar POS Upgrade | Khaled / Omar | **Near limit** (85 %) |
| PRJ-0004 Atlas Network Monitoring | Khaled / Yousef | On track now (68.75 %) but **projected to exceed** — pending hours would push it 5 h over |
| PRJ-0005 Oasis Site Planning | Khaled / Hala | **Completed**; its client is inactive |
| PRJ-0006 BlueWave Claims Portal | Khaled / Tariq | **On hold** — nobody can log hours |
| PRJ-0007 Cedar Loyalty App | Rania / Lina | **Reopened** entry, **director-rejected** entry, entry **resubmitted** after rejection, cancelled then re-logged |
| PRJ-0008 Atlas Billing Migration | Rania / Yousef | **Archived**, exactly on budget |
| PRJ-0009 Sunrise Lab Integration | Rania / Hala | Brand new — no hours, budget not locked |
| PRJ-0010 Horizon Warehouse Robotics | Rania / Tariq | 6 entries waiting for Tariq — **bulk approve** |

---

## Opening (1 min)

> "It's a timesheet API: employees log hours on projects, the manager approves first, the director approves
> second, and every project's budget is tracked live. .NET 10, EF Core and SQL Server, JWT auth, four roles,
> a layered design, and 346 automated tests."

Click the collection name to show the documentation page: folders per role, one error format, paging.

---

## Act 1 — the director's morning (Khaled)

1. **Login** `khaled` → **Director → Dashboard**
   - 5 entries waiting for his final approval
   - budget alerts: 1 exceeded, 1 projected to exceed, 2 near the limit
2. **Reports → Portfolio** with `pageSize=3`
   - problems first: **PRJ-0002 Exceeded**, then **PRJ-0004** (only 69 % used, but its pending hours would push
     it over), then the near-limit projects
   - point at `hasNextPage: true` — *"every list is paged"*
3. **ProjectSummary** for PRJ-0002 → `exceededBy: 15`.
   Then PRJ-0004: `remainingHours: 50` but `projectedRemaining: -5` —
   *"50 h left today; 5 h over if everything waiting is approved."*
4. **DailyReport** for PRJ-0001 with `groupBy=week` and a `from` about three weeks back
   - `openingBalance` = hours approved before that date, so the first row's running total doesn't start at 0
   - only weeks where people actually worked are listed
   - `closingBalance` = all approved hours (290)
5. **EmployeesReport** for PRJ-0001 → Noor has `isActiveOnProject: false` — *"moved off the team; her history stays."*
6. **ReportPdf** for PRJ-0002 → **Send and Download** → open the PDF.

## Act 2 — an employee logs hours (Ahmad)

1. **Login** `ahmad` → **Employee → Dashboard** — 4 entries pending, 0 hours today.
2. **DropdownMyProjects** → Sunrise Patient Portal, Atlas Network Monitoring.
3. **SubmitEntry**: PRJ-0001, today, 8 h → `201`, `PendingManager`. Copy the id.
4. Show the rules:
   - same request again → `400` *"You already have an entry … Edit that entry instead."*
   - PRJ-0004, today, 5 h → `400` *"Daily limit is 12 h. You already have 8 h … at most 4 h."*
   - tomorrow's date → `400` *"You can't log hours for a future date."*
5. **GetMyEntries** with `pageSize=5` → paging.

## Act 3 — fixing a rejection (Mohammad)

1. **Login** `mohammad` → **Dashboard** → `rejectedCount: 1`.
2. **GetMyEntries** with `status=Rejected` → rejected by Omar: *"… 'misc' is not enough to approve."*
3. **GetEntryHistory** → who rejected it and when.
4. **UpdateEntry** (same project, date and hours; real notes) → back to `PendingManager`.
   *"Editing a rejected entry resubmits it."*

## Act 4 — managers review (Omar, then Tariq)

1. **Login** `omar` → **Dashboard** → queue of 5 (Ahmad's and Mohammad's new entries included) → **GetPending**.
2. **Approve** Mohammad's entry and Ahmad's new entry.
3. **Login** `tariq` → **GetPending** → 6 entries on Horizon Warehouse Robotics.
4. **BulkApprove** with those 6 ids plus one made-up id → 6 in `approved`, the made-up one in `failed`
   with *"Time entry not found."* — *"each entry is processed on its own."*

## Act 5 — final approval, budget lock and reopen (Khaled)

1. **Login** `khaled` → **GetPending**
   - Faris's entry on PRJ-0002 shows `wouldExceedBudget: true`
   - Omar's approvals arrived, with `managerApprovedBy`
2. **Approve** Mohammad's entry.
3. **Projects → GetProjectById** PRJ-0001 → `isBudgetLocked: true`.
   **UpdateProject** with a different budget → `400` *"The budget is locked …"* —
   *"the budget is the client contract; once hours are approved against it, it can't change."*
4. **Reopen** Mohammad's entry with a comment → `Rejected`; the hours leave the actuals; Mohammad sees the
   director's comment.

## Act 6 — the other cases (Rania, Faris, Noor)

1. **Login** `rania` → **Portfolio** → PRJ-0008 Archived at exactly 100 %.
   PRJ-0009 is brand new → **UpdateProject** with a new budget succeeds (nothing approved yet) — the contrast to Act 5.
2. **Login** `faris` → **GetMyEntries** with `status=Rejected` → `rejectedAtLevel: Director`;
   **GetEntryHistory** → Approved → Approved → **Reopened**.
3. **Login** `noor` → **DropdownMyProjects** → only PRJ-0002 and PRJ-0009
   (PRJ-0006 is on hold, PRJ-0001 removed her).
   **SubmitEntry** on PRJ-0006 → `400` *"… PRJ-0006 is OnHold."*

## Act 7 — security

- **Rania** → **GetProjectById** PRJ-0001 (Khaled's) → `404` — *"other people's data acts as if it doesn't exist."*
- **Lina** → **Approve** one of Omar's entries → `404`.
- **Ahmad** → any Director request → `403`.

## Act 8 — setting up projects (Khaled)

- **CreateProject** named "Sunrise Patient Portal" → `400` *"… already exists (PRJ-0001)."*
- **AssignEmployees** PRJ-0001 with Ahmad → `400` *"Already assigned to this project: Ahmad Khalil …"*
- **CreateProject** with a new name, Hala as manager and two employees → `201`, **PRJ-0011**.

## Closing (1 min)

> "346 tests: unit tests for every allowed and blocked status change and every controller's role guard, and
> integration tests on a real SQL Server — the database constraints, race conditions like two approvals at the
> same moment, and a test that the demo data itself follows every rule."

Run `dotnet test` live if there's time.

---

## Likely questions

| Question | Answer |
|---|---|
| Why 404 and not 403 for someone else's project? | So users can't discover which projects exist. 403 is only for the wrong role. |
| What if two people approve at once? | A RowVersion check stops the second save ("already reviewed"); the daily cap, duplicate day and project name use serializable transactions. The tests fire real parallel requests to prove it. |
| Why don't you store actual hours? | Always computed from approved entries, so they can't drift — reopening an entry updates them instantly. |
| What's the difference between `remainingHours` and `projectedRemaining`? | Remaining = budget − approved (now). Projected = also minus everything pending — an early warning before the hours are approved. |
| What is the opening balance? | Approved hours before the report's `from` date, so running totals stay correct when you look at only part of a project. |
| Why are clients and team members never deleted? | History and reports must stay correct — they're deactivated instead. |
| How does the frontend learn about new approvals? | Dashboards are cheap counts the frontend polls; SignalR was a deliberate no. |
| Where are the business rules? | In the BLL services — one state machine in `ApprovalService`, one visibility rule in `ProjectAccessService`. Controllers stay thin. |
