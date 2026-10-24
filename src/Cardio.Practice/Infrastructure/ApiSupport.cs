using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Cardio.Contracts;
using Cardio.Data;
using Cardio.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Cardio.Infrastructure;

public class ApiError(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

public class ValidationFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext c,
        EndpointFilterDelegate next)
    {
        var errors = new Dictionary<string, string[]>();

        foreach (var input in c.Arguments.OfType<IRequest>())
        {
            Check(input, "", errors);

            if (input is DocumentInput d && d.Lines is not null)
            {
                for (var i = 0; i < d.Lines.Count; i++)
                {
                    if (d.Lines[i] is null)
                        errors[$"Lines[{i}]"] = ["Required."];
                    else
                        Check(d.Lines[i], $"Lines[{i}].", errors);
                }
            }
        }

        return errors.Count > 0
            ? Results.ValidationProblem(errors)
            : await next(c);
    }

    private static void Check(
        object input,
        string prefix,
        Dictionary<string, string[]> errors)
    {
        var results = new List<ValidationResult>();

        Validator.TryValidateObject(
            input,
            new ValidationContext(input),
            results,
            true);

        foreach (var r in results)
        {
            var key = prefix + (r.MemberNames.FirstOrDefault() ?? "input");
            errors[key] = [r.ErrorMessage!];
        }
    }
}

public class PracticeAccess(
    PracticeDb db,
    IHttpContextAccessor http)
{
    public ClaimsPrincipal Principal => http.HttpContext!.User;

    public string UserId =>
        Principal.FindFirstValue(ClaimTypes.NameIdentifier)!;

    public bool Staff =>
        Principal.IsInRole("Doctor") ||
        Principal.IsInRole("Receptionist") ||
        Principal.IsInRole("Nurse");

    public bool Care =>
        Principal.IsInRole("Doctor") ||
        Principal.IsInRole("Nurse");

    public async Task<Patient> Patient(Guid id)
    {
        var p = await db.Patients.FindAsync(id);

        if (p is null || (!Staff && p.AccountId != UserId))
            throw new ApiError(404, "Patient not found.");

        return p;
    }

    public async Task<Patient> OwnPatient() =>
        await db.Patients.SingleOrDefaultAsync(x => x.AccountId == UserId)
        ?? throw new ApiError(
            403,
            "Reception must verify and link your medical record.");

    public async Task Save(string action, Guid? patientId = null)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            ActorId = UserId,
            Action = action,
            PatientId = patientId
        });

        await db.SaveChangesAsync();
    }

    public void Notify(Guid patientId, string kind, Guid referenceId)
    {
        db.Notifications.Add(new Notification
        {
            PatientId = patientId,
            Kind = kind,
            ReferenceId = referenceId
        });
    }

    public static void Require(bool condition, string message)
    {
        if (!condition)
            throw new ApiError(400, message);
    }
}

public class ActiveAccountFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext c,
        EndpointFilterDelegate next)
    {
        var users = c.HttpContext.RequestServices
            .GetRequiredService<UserManager<User>>();

        var user = await users.GetUserAsync(c.HttpContext.User);

        if (user is null || !user.Enabled)
            return Results.Unauthorized();

        return await next(c);
    }
}

public static class ApiErrors
{
    public static async Task Handle(
        HttpContext c,
        RequestDelegate next)
    {
        try
        {
            await next(c);
        }
        catch (Exception e)
        {
            var pg = e as PostgresException
                ?? e.InnerException as PostgresException;

            var status = e is ApiError a
                ? a.Status
                : e is DbUpdateConcurrencyException ||
                  pg?.SqlState is "23505" or "23P01" or "40001" or "40P01"
                    ? 409
                    : e is BadHttpRequestException
                        ? 400
                        : 500;

            var message = e is ApiError error
                ? error.Message
                : status == 409
                    ? "Conflict. Refresh the data before retrying."
                    : status == 400
                        ? "Invalid request."
                        : "The operation could not be completed.";

            if (status == 500)
            {
                c.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("ApiErrors")
                    .LogError(
                        "Request failed: {ExceptionType}",
                        e.GetType().Name);
            }

            c.Response.StatusCode = status;

            await Results.Problem(
                statusCode: status,
                title: message).ExecuteAsync(c);
        }
    }
}
