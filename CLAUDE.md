# TimeSheet API — instructions for Claude

## Read first
- `docs/DESIGN.md` is the source of truth for entities, rules, workflow and endpoints. Follow it; if something needs to change, say so and ask before deviating.
- Work one build-order step (DESIGN.md §8) per session unless told otherwise.

## Git
- **Never run `git commit`.** Stage changes or leave them for Abood to commit himself.

## Stack
- .NET 10, ASP.NET Core Web API, EF Core + SQL Server, ASP.NET Identity + JWT, Mapster, QuestPDF, Scalar, xUnit + Moq + MockQueryable + FluentAssertions.
- No SignalR.

## Conventions (same as Brainova)
- N-tier: `TimeSheet.DAL` / `TimeSheet.BLL` / `TimeSheet.PL` / `TimeSheet.Tests`.
- Entities inherit `BaseEntity` (`Guid Id`, `CreatedAt`, `UpdatedAt`); `AppDbContext` sets them in `SaveChanges`.
- One `IEntityTypeConfiguration<T>` per entity in `DAL/Data/Configs`, applied in `OnModelCreating`.
- Data access through `IUnitOfWork.Repo<T>()` + `IGenericRepository<T>`; multi-step writes inside `ExecuteInTransactionAsync`.
- Enums stored as int with explicit pinned values.
- FKs to users use `DeleteBehavior.Restrict`.
- Business rules live in BLL services, not controllers. Errors are thrown as `BadRequestException` / `NotFoundException` / `ForbiddenException` / `UnauthorizedException`; `ApiExceptionMiddleware` returns `{ success: false, message }`.
- Controllers: `[ApiController]`, `[Area("Director")]` etc., `[Route("api/[area]/[controller]")]`, `[Authorize(Roles = "...")]`. Get the user id with `User.GetUserId()`.
- DTOs in `BLL/DTOs/Request` and `BLL/DTOs/Response`; mapping with Mapster.
- Every service has an interface in `Services/Interface` and is registered as Scoped in `Program.cs`.
- Configuration values (time zone, week start, daily cap, backdate days) come from `TimeSheetSettings`, never hard-coded.
- "Today" is computed in the business time zone, not server UTC.
- Secrets (connection string, JWT key) via user-secrets / environment variables, never in appsettings.

## Before finishing a session
- `dotnet build` passes.
- New EF changes have a migration.
- Tests added for any new business rule.
- Summarize what changed and what the next step is.
