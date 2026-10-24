using Cardio.Contracts;
using Cardio.Data;
using Cardio.Domain;
using Cardio.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using static Cardio.Infrastructure.PracticeAccess;

namespace Cardio.Endpoints;

public static class PatientEndpoints
{
    public static void MapPatients(this RouteGroupBuilder api)
    {
        api.MapGet("/me", async (PracticeDb db, PracticeAccess a) =>
            Results.Ok(new
            {
                userId = a.UserId,

                roles = a.Principal.Claims
                    .Where(x =>
                        x.Type == System.Security.Claims.ClaimTypes.Role)
                    .Select(x => x.Value),

                patientId = await db.Patients
                    .Where(x => x.AccountId == a.UserId)
                    .Select(x => (Guid?)x.Id)
                    .SingleOrDefaultAsync()
            }));

        api.MapPost("/staff", async (
            StaffInput input,
            UserManager<User> users,
            PracticeAccess a) =>
        {
            Require(
                input.Role is "Nurse" or "Receptionist",
                "Invalid staff role.");

            var u = new User
            {
                UserName = input.Email,
                Email = input.Email,
                DisplayName = input.Name
            };

            var result = await users.CreateAsync(u, input.Password);

            Require(
                result.Succeeded,
                "Account creation failed. Check email and password requirements.");

            var role = await users.AddToRoleAsync(u, input.Role);

            if (!role.Succeeded)
            {
                await users.DeleteAsync(u);
                throw new ApiError(500, "Staff role assignment failed.");
            }

            await a.Save("staff.create");

            return Results.Ok(new
            {
                u.Id,
                u.DisplayName,
                input.Role
            });
        }).RequireAuthorization("Doctor");

        api.MapGet("/patients", async (
            PracticeDb db,
            PracticeAccess a) =>
        {
            var rows = await db.Patients
                .AsNoTracking()
                .OrderByDescending(x => x.CreatedAt)
                .Take(200)
                .ToListAsync();

            await a.Save("patients.list");

            return Results.Ok(rows);
        }).RequireAuthorization("Staff");

        api.MapPost("/patients", async (
            PatientInput input,
            PracticeDb db,
            PracticeAccess a) =>
        {
            Require(
                input.BirthDate >= new DateOnly(1900, 1, 1) &&
                input.BirthDate <= DateOnly.FromDateTime(DateTime.UtcNow),
                "Invalid birth date.");

            var p = new Patient
            {
                Name = input.Name.Trim(),
                BirthDate = input.BirthDate,
                Phone = input.Phone.Trim()
            };

            db.Patients.Add(p);
            await a.Save("patient.create", p.Id);

            return Results.Created($"/api/patients/{p.Id}", p);
        }).RequireAuthorization("Office");

        api.MapGet("/patients/{id:guid}", async (
            Guid id,
            PracticeAccess a) =>
        {
            var p = await a.Patient(id);
            await a.Save("patient.read", id);

            return Results.Ok(p);
        });

        api.MapPost("/patients/{id:guid}/link", async (
            Guid id,
            LinkInput input,
            PracticeDb db,
            PracticeAccess a,
            UserManager<User> users) =>
        {
            var p = await a.Patient(id);

            Require(
                input.IdentityChecked && p.AccountId is null,
                "Verify identity; record must be unlinked.");

            var u = await users.FindByIdAsync(input.UserId);

            Require(
                u is { Enabled: true } &&
                !(await users.GetRolesAsync(u)).Any(),
                "Use a patient account.");

            Require(
                !await db.Patients.AnyAsync(
                    x => x.AccountId == input.UserId),
                "Account already linked.");

            p.AccountId = input.UserId;

            await a.Save("patient.link.verified", id);

            return Results.NoContent();
        }).RequireAuthorization("Doctor");

        api.MapGet("/me/notifications", async (
            PracticeAccess a,
            PracticeDb db) =>
        {
            var p = await a.OwnPatient();

            return Results.Ok(
                await db.Notifications
                    .AsNoTracking()
                    .Where(x => x.PatientId == p.Id)
                    .OrderByDescending(x => x.CreatedAt)
                    .Take(100)
                    .ToListAsync());
        });
    }
}
