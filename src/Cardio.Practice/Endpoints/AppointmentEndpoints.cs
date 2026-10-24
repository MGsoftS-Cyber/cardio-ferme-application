using Cardio.Contracts;
using Cardio.Data;
using Cardio.Domain;
using Cardio.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Cardio.Infrastructure.PracticeAccess;

namespace Cardio.Endpoints;

public static class AppointmentEndpoints
{
    public static void MapAppointments(this RouteGroupBuilder api)
    {
        api.MapPost("/slots", async (
            SlotInput input,
            PracticeDb db,
            PracticeAccess a) =>
        {
            var start = input.Start.ToUniversalTime();
            var end = input.End.ToUniversalTime();

            Require(
                start > DateTimeOffset.UtcNow &&
                end > start &&
                end - start <= TimeSpan.FromHours(3),
                "Provide a future slot of at most three hours.");

            var slot = new Slot
            {
                DoctorId = a.UserId,
                Start = start,
                End = end,
                ResourceKey = string.IsNullOrWhiteSpace(input.ResourceKey)
                    ? null
                    : input.ResourceKey.Trim()
            };

            db.Slots.Add(slot);
            await a.Save("slot.create");

            return Results.Ok(slot);
        }).RequireAuthorization("Doctor");

        api.MapGet("/slots", async (PracticeDb db) =>
            Results.Ok(
                await db.Slots
                    .AsNoTracking()
                    .Where(s =>
                        !s.Closed &&
                        s.Start > DateTimeOffset.UtcNow &&
                        !db.Appointments.Any(x =>
                            x.SlotId == s.Id &&
                            x.Status != "Cancelled"))
                    .OrderBy(x => x.Start)
                    .Take(200)
                    .ToListAsync()));

        api.MapPost("/slots/{id:guid}/close", async (
            Guid id,
            PracticeDb db,
            PracticeAccess a) =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();

            var slot = await db.Slots
                .FromSqlInterpolated(
                    $"SELECT * FROM \"Slots\" WHERE \"Id\" = {id} FOR UPDATE")
                .SingleOrDefaultAsync()
                ?? throw new ApiError(404, "Slot not found.");

            Require(
                slot.DoctorId == a.UserId,
                "Only the slot's doctor can close it.");

            // Closing a slot does not cancel an existing appointment.
            slot.Closed = true;

            await a.Save("slot.close");
            await tx.CommitAsync();

            return Results.NoContent();
        }).RequireAuthorization("Doctor");

        api.MapPost("/appointments", async (
            BookingInput input,
            PracticeDb db,
            PracticeAccess a) =>
        {
            Require(
                input.RequestKey != Guid.Empty,
                "A requestKey is required.");

            var p = input.PatientId is Guid pid
                ? await a.Patient(pid)
                : await a.OwnPatient();

            var existing = await db.Appointments
                .AsNoTracking()
                .SingleOrDefaultAsync(x =>
                    x.RequestKey == input.RequestKey);

            if (existing is not null)
            {
                if (existing.PatientId != p.Id ||
                    existing.SlotId != input.SlotId)
                {
                    throw new ApiError(
                        409,
                        "Request key already used for a different booking.");
                }

                return Results.Ok(existing);
            }

            await using var tx = await db.Database.BeginTransactionAsync();

            var slot = await db.Slots
                .FromSqlInterpolated(
                    $"SELECT * FROM \"Slots\" WHERE \"Id\" = {input.SlotId} FOR UPDATE")
                .SingleOrDefaultAsync()
                ?? throw new ApiError(404, "Slot not found.");

            // Recheck after obtaining the lock for simultaneous retries.
            existing = await db.Appointments
                .AsNoTracking()
                .SingleOrDefaultAsync(x =>
                    x.RequestKey == input.RequestKey);

            if (existing is not null)
            {
                if (existing.PatientId != p.Id ||
                    existing.SlotId != input.SlotId)
                {
                    throw new ApiError(
                        409,
                        "Request key already used for a different booking.");
                }

                return Results.Ok(existing);
            }

            if (slot.Closed ||
                slot.Start <= DateTimeOffset.UtcNow ||
                await db.Appointments.AnyAsync(x =>
                    x.SlotId == slot.Id &&
                    x.Status != "Cancelled"))
            {
                throw new ApiError(409, "This slot is unavailable.");
            }

            var booking = new Appointment
            {
                PatientId = p.Id,
                SlotId = slot.Id,
                Start = slot.Start,
                End = slot.End,
                RequestKey = input.RequestKey
            };

            db.Appointments.Add(booking);
            a.Notify(p.Id, "AppointmentConfirmed", booking.Id);

            await a.Save("appointment.book", p.Id);
            await tx.CommitAsync();

            return Results.Ok(booking);
        });

        api.MapGet("/appointments", async (
            PracticeDb db,
            PracticeAccess a) =>
        {
            var q = db.Appointments.AsNoTracking();

            if (!a.Staff)
            {
                var p = await a.OwnPatient();
                q = q.Where(x => x.PatientId == p.Id);
            }

            var rows = await q
                .OrderByDescending(x => x.Start)
                .Take(200)
                .ToListAsync();

            await a.Save("appointments.read");

            return Results.Ok(rows);
        });

        api.MapPost("/appointments/{id:guid}/status", async (
            Guid id,
            StatusInput input,
            PracticeDb db,
            PracticeAccess a) =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();

            var row = await db.Appointments
                .FromSqlInterpolated(
                    $"SELECT * FROM \"Appointments\" WHERE \"Id\" = {id} FOR UPDATE")
                .SingleOrDefaultAsync()
                ?? throw new ApiError(404, "Appointment not found.");

            await a.Patient(row.PatientId);

            if (!a.Staff)
            {
                Require(
                    input.Status == "Cancelled" &&
                    row.Status == "Confirmed" &&
                    row.Start > DateTimeOffset.UtcNow.AddHours(2),
                    "Online cancellation closes two hours before the visit.");
            }

            var allowed = row.Status switch
            {
                "Confirmed" => new[]
                {
                    "CheckedIn", "Cancelled", "NoShow"
                },
                "CheckedIn" => new[]
                {
                    "InProgress", "Cancelled"
                },
                "InProgress" => new[]
                {
                    "Completed"
                },
                _ => Array.Empty<string>()
            };

            Require(
                allowed.Contains(input.Status),
                "Invalid appointment transition.");

            Require(
                input.Status != "NoShow" ||
                row.Start <= DateTimeOffset.UtcNow,
                "Visit has not started.");

            row.Status = input.Status;

            a.Notify(row.PatientId, "AppointmentUpdated", row.Id);

            await a.Save("appointment.status", row.PatientId);
            await tx.CommitAsync();

            return Results.Ok(row);
        });
    }
}
