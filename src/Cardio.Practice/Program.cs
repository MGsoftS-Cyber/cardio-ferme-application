using System.Threading.RateLimiting;
using Cardio.Data;
using Cardio.Domain;
using Cardio.Endpoints;
using Cardio.Infrastructure;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(
    args.Where(x => x != "--bootstrap").ToArray());

builder.Services.AddDbContext<PracticeDb>(o =>
    o.UseNpgsql(
        builder.Configuration.GetConnectionString("Practice")
        ?? throw new InvalidOperationException(
            "Set ConnectionStrings:Practice.")));

builder.Services
    .AddIdentityApiEndpoints<User>(o =>
    {
        o.Password.RequiredLength = 12;
        o.User.RequireUniqueEmail = true;

        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<PracticeDb>();

builder.Services.Configure<BearerTokenOptions>(
    IdentityConstants.BearerScheme,
    o =>
    {
        o.BearerTokenExpiration = TimeSpan.FromMinutes(15);
        o.RefreshTokenExpiration = TimeSpan.FromHours(8);
    });

builder.Services.AddAuthorization(o =>
{
    o.AddPolicy(
        "Api",
        new AuthorizationPolicyBuilder(IdentityConstants.BearerScheme)
            .RequireAuthenticatedUser()
            .Build());

    o.AddPolicy("Doctor", p =>
        p.RequireRole("Doctor"));

    o.AddPolicy("Care", p =>
        p.RequireRole("Doctor", "Nurse"));

    o.AddPolicy("Staff", p =>
        p.RequireRole("Doctor", "Nurse", "Receptionist"));

    o.AddPolicy("Office", p =>
        p.RequireRole("Doctor", "Receptionist"));

    o.AddPolicy("OfficeOrPatient", p =>
        p.RequireAssertion(c =>
            c.User.IsInRole("Doctor") ||
            c.User.IsInRole("Receptionist") ||
            !c.User.Claims.Any(x => x.Type == System.Security.Claims.ClaimTypes.Role)));
});

builder.Services
    .AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(
        builder.Configuration["Keys:Root"] ??
        Path.Combine(
            builder.Environment.ContentRootPath,
            "PrivateData",
            "keys")))
    .SetApplicationName("CardioPractice");

builder.Services.AddRazorPages();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<PracticeAccess>();
builder.Services.AddScoped<PrivateFiles>();

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;

    o.AddPolicy("auth", c =>
        RateLimitPartition.GetFixedWindowLimiter(
            c.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

builder.WebHost.ConfigureKestrel(o =>
    o.Limits.MaxRequestBodySize = 11 * 1024 * 1024);

var app = builder.Build();

if (args.Contains("--bootstrap"))
{
    await Bootstrap.Run(app.Services, app.Configuration);
    return;
}

app.Use(ApiErrors.Handle);

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.Use(async (c, next) =>
{
    c.Response.Headers["Cache-Control"] = "no-store";
    c.Response.Headers["X-Content-Type-Options"] = "nosniff";
    c.Response.Headers["Referrer-Policy"] = "no-referrer";

    c.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; script-src 'self'; style-src 'self'; " +
        "object-src 'none'; frame-ancestors 'none'; base-uri 'self'";

    await next(c);
});

app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGroup("/auth")
    .RequireRateLimiting("auth")
    .MapIdentityApi<User>();

var api = app.MapGroup("/api")
    .RequireAuthorization("Api")
    .AddEndpointFilter<ActiveAccountFilter>()
    .AddEndpointFilter<ValidationFilter>();

api.MapPatients();
api.MapAppointments();
api.MapClinical();
api.MapOffice();
PrivateFiles.MapFiles(api);

app.MapRazorPages();

app.MapGet("/health", async (PracticeDb db) =>
    await db.Database.CanConnectAsync() &&
    !(await db.Database.GetPendingMigrationsAsync()).Any()
        ? (IResult)Results.Ok(new { status = "ok" })
        : Results.StatusCode(503));

app.Run();
