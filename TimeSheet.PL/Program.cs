using Mapster;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using System.Text.Json.Serialization;
using TimeSheet.BLL.Exceptions;
using TimeSheet.BLL.Mapping;
using TimeSheet.BLL.Services.Classes;
using TimeSheet.BLL.Services.Interface;
using TimeSheet.BLL.Settings;
using TimeSheet.DAL.Data;
using TimeSheet.DAL.Models;
using TimeSheet.DAL.Repositories.Classes;
using TimeSheet.DAL.Repositories.Interface;
using TimeSheet.DAL.Utilities;

var builder = WebApplication.CreateBuilder(args);
TypeAdapterConfig.GlobalSettings.Scan(typeof(MapsterConfig).Assembly);

// Configure for cloud deployment (Render, Azure App Service, Docker, etc.)
// Only apply this in non-Development so launchSettings.json (and Scalar) work locally.
if (!builder.Environment.IsDevelopment())
{
    var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

// ==============================
// Forwarded headers — REQUIRED when running behind a TLS-terminating proxy.
// Without this, ASP.NET sees Request.Scheme = "http" even though the client is on HTTPS.
// ==============================
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
                             | ForwardedHeaders.XForwardedProto
                             | ForwardedHeaders.XForwardedHost;
    // Managed PaaS: the proxy IP isn't known/static.
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// ==============================
// Settings
// ==============================
builder.Services.AddOptions<TimeSheetSettings>()
    .Bind(builder.Configuration.GetSection(TimeSheetSettings.SectionName))
    .Validate(s => !string.IsNullOrWhiteSpace(s.BusinessTimeZone)
                   && TimeZoneInfo.TryFindSystemTimeZoneById(s.BusinessTimeZone, out _),
        "TimeSheetSettings:BusinessTimeZone is missing or not a known time zone.")
    .Validate(s => s.MaxDailyHours > 0, "TimeSheetSettings:MaxDailyHours must be > 0.")
    .Validate(s => s.MaxBackdateDays >= 0, "TimeSheetSettings:MaxBackdateDays must be >= 0.")
    .ValidateOnStart();

// ==============================
// Services
// ==============================
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<ISeedData, SeedData>();
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IClientService, ClientService>();
builder.Services.AddScoped<IProjectAccessService, ProjectAccessService>();
builder.Services.AddScoped<IProjectService, ProjectService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IBusinessClock, BusinessClock>();
builder.Services.AddScoped<ITimeEntryService, TimeEntryService>();
builder.Services.AddScoped<IApprovalService, ApprovalService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<IReportPdfService, ReportPdfService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
        policy
            .AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod()
    );
});

// Database configuration - environment variables first, then user-secrets / appsettings
var connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING")
    ?? builder.Configuration.GetConnectionString("Default");

if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException(
        "No database connection string is configured. " +
        "Local development: dotnet user-secrets set \"ConnectionStrings:Default\" \"<connection string>\" --project TimeSheet.PL. " +
        "Production: set the DB_CONNECTION_STRING (or ConnectionStrings__Default) environment variable.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString, sql =>
        sql.EnableRetryOnFailure(
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(5),
            errorNumbersToAdd: null)));

// ==============================
// Identity
// ==============================
builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;

        options.User.RequireUniqueEmail = true;

        // Accounts are created by an Admin; there is no email confirmation flow.
        options.SignIn.RequireConfirmedEmail = false;

        // lockout (still useful for brute-force even if you also have IsBlocked)
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(1);
        options.Lockout.MaxFailedAccessAttempts = 5;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

// ==============================
// JWT Auth (Base64 Secret + RoleClaimType = "Role", user id in "Id")
// ==============================
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        var jwt = builder.Configuration.GetSection("jwtOptions");

        var secretBase64 = jwt["SecretKey"];
        if (string.IsNullOrWhiteSpace(secretBase64))
            throw new InvalidOperationException(
                "No JWT signing key is configured. " +
                "Local development: dotnet user-secrets set \"jwtOptions:SecretKey\" \"<base64 key>\" --project TimeSheet.PL. " +
                "Production: set the jwtOptions__SecretKey environment variable. " +
                "Generate one with: openssl rand -base64 32.");

        var keyBytes = Convert.FromBase64String(secretBase64);

        // Keep our custom claim names ("Id", "Role") as-is.
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(keyBytes),

            ValidateIssuer = true,
            ValidIssuer = jwt["Issuer"],

            ValidateAudience = true,
            ValidAudience = jwt["Audience"],

            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,

            // IMPORTANT: roles are added as new Claim("Role", role)
            RoleClaimType = "Role",
            NameClaimType = "Name"
        };

        options.Events = new JwtBearerEvents
        {
            // A token stays valid until it expires, so re-check the user on every
            // request: blocked, deleted or re-roled users are rejected with 401.
            OnTokenValidated = async context =>
            {
                var userId = context.Principal?.FindFirst("Id")?.Value;
                var role = context.Principal?.FindFirst("Role")?.Value;

                var auth = context.HttpContext.RequestServices.GetRequiredService<IAuthenticationService>();
                if (string.IsNullOrEmpty(userId) || !await auth.IsUserActiveAsync(userId, role))
                    context.Fail("The account is blocked, deleted, or its role has changed. Please log in again.");
            }
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddHttpContextAccessor();

builder.Services.AddControllers()
    // Enums travel as names in JSON ("OnHold", not 1); numbers are still accepted on input.
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// ==============================
// Model-validation errors (DataAnnotations) run BEFORE the controller/exception
// middleware, so by default ASP.NET returns its ProblemDetails shape. Override it
// to match our standard error body: { "success": false, "message": "..." }
// ==============================
builder.Services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(kvp => kvp.Value?.Errors.Count > 0)
            .ToList();

        // A body that failed to deserialize (e.g. "status": "Paused") is reported under a
        // JSON-path key like "$.status"; name that field instead of the generic
        // "The request field is required." that comes with it.
        var jsonError = errors.FirstOrDefault(kvp => kvp.Key.StartsWith('$'));
        var jsonField = jsonError.Key?.TrimStart('$', '.');

        var message = jsonError.Key is null
            ? errors
                .SelectMany(kvp => kvp.Value!.Errors)
                .Select(e => e.ErrorMessage)
                .FirstOrDefault(m => !string.IsNullOrWhiteSpace(m))
              ?? "Invalid request."
            : string.IsNullOrEmpty(jsonField)
                ? "The request body is not valid JSON."
                : $"Invalid value for '{jsonField}'.";

        return new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(new
        {
            success = false,
            message
        });
    };
});

builder.Services.AddOpenApi();

var app = builder.Build();

// MUST be the first middleware so everything after sees the forwarded scheme/host/IP.
app.UseForwardedHeaders();

app.UseCors("AllowFrontend");

app.UseMiddleware<ApiExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

// Startup seeding must never take the whole API down.
// If the database is unreachable, log it loudly and keep serving.
using (var scope = app.Services.CreateScope())
{
    var startupLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        var seed = scope.ServiceProvider.GetRequiredService<ISeedData>();
        await seed.DataSeedingAsync();          // migrate
        await seed.IdentitySeedingAsync();      // roles

        if (app.Environment.IsDevelopment())
            await seed.DemoDataSeedingAsync();  // demo users + clients

        startupLogger.LogInformation("Database migration and seeding completed successfully.");
    }
    catch (Exception ex)
    {
        startupLogger.LogCritical(ex,
            "Database migration/seeding FAILED at startup. The API is starting anyway, but " +
            "every database-backed endpoint will fail until this is resolved.");
    }
}

// HTTPS redirection intentionally NOT enabled: the platform proxy terminates TLS.

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
