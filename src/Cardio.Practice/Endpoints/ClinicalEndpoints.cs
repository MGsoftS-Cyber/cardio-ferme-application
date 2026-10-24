using System.Text.Encodings.Web;
using Cardio.Contracts;
using Cardio.Data;
using Cardio.Domain;
using Cardio.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using static Cardio.Infrastructure.PracticeAccess;

namespace Cardio.Endpoints;

public static class ClinicalEndpoints
{
    public static void MapClinical(this RouteGroupBuilder api)
    {
        api.MapGet("/patients/{id:guid}/entries", async (
            Guid id,
            PracticeDb db,
            PracticeAccess a) =>
        {
            await a.Patient(id);

            var rows = await db.Entries
                .AsNoTracking()
                .Where(x => x.PatientId == id)
                .OrderByDescending(x => x.ObservedAt)
                .Take(300)
                .ToListAsync();

            await a.Save("clinical.entries.read", id);

            return Results.Ok(rows);
        }).RequireAuthorization("Care");

        api.MapPost("/patients/{id:guid}/entries", async (
            Guid id,
            EntryInput input,
            PracticeDb db,
            PracticeAccess a) =>
        {
            await a.Patient(id);

            Require(
                new[]
                {
                    "History", "Allergy", "Medication",
                    "Observation", "Laboratory", "Examination"
                }.Contains(input.Kind),
                "Invalid clinical entry kind.");

            Require(
                !input.Value.HasValue ||
                !string.IsNullOrWhiteSpace(input.Unit),
                "A numeric result needs its unit.");

            Require(
                input.ObservedAt != default &&
                input.ObservedAt <= DateTimeOffset.UtcNow.AddMinutes(5),
                "Invalid observation date.");

            var row = new ClinicalEntry
            {
                PatientId = id,
                AuthorId = a.UserId,
                Kind = input.Kind,
                Title = input.Title,
                Content = input.Content,
                Value = input.Value,
                Unit = input.Unit,
                ObservedAt = input.ObservedAt.ToUniversalTime()
            };

            db.Entries.Add(row);
            await a.Save("clinical.entry.create", id);

            return Results.Ok(row);
        }).RequireAuthorization("Care");

        api.MapGet("/patients/{id:guid}/consultations", async (
            Guid id,
            PracticeDb db,
            PracticeAccess a) =>
        {
            await a.Patient(id);

            var rows = await db.Consultations
                .AsNoTracking()
                .Where(x => x.PatientId == id)
                .OrderByDescending(x => x.CreatedAt)
                .Take(100)
                .ToListAsync();

            await a.Save("consultations.read", id);

            return Results.Ok(rows);
        }).RequireAuthorization("Doctor");

        api.MapPost("/patients/{id:guid}/consultations", async (
            Guid id,
            NoteInput input,
            PracticeDb db,
            PracticeAccess a) =>
        {
            await a.Patient(id);

            var row = new Consultation
            {
                PatientId = id,
                DoctorId = a.UserId,
                Note = input.Note
            };

            db.Consultations.Add(row);
            await a.Save("consultation.create", id);

            return Results.Ok(row);
        }).RequireAuthorization("Doctor");

        api.MapPut("/consultations/{id:guid}", async (
            Guid id,
            NoteInput input,
            PracticeDb db,
            PracticeAccess a) =>
        {
            var row = await db.Consultations.FindAsync(id)
                ?? throw new ApiError(404, "Consultation not found.");

            Require(
                row.DoctorId == a.UserId && row.SignedAt is null,
                "Only the author can edit an unsigned note.");

            if (row.Version != input.Version)
                throw new ApiError(409, "The note has changed.");

            row.Note = input.Note;
            row.Version = Guid.NewGuid();

            await a.Save("consultation.update", row.PatientId);

            return Results.Ok(row);
        }).RequireAuthorization("Doctor");

        api.MapPost("/consultations/{id:guid}/sign", async (
            Guid id,
            PracticeDb db,
            PracticeAccess a) =>
        {
            var row = await db.Consultations.FindAsync(id)
                ?? throw new ApiError(404, "Consultation not found.");

            Require(
                row.DoctorId == a.UserId && row.SignedAt is null,
                "Only the author can finalize a draft.");

            row.SignedAt = DateTimeOffset.UtcNow;
            row.Version = Guid.NewGuid();

            await a.Save("consultation.sign", row.PatientId);

            return Results.Ok(row);
        }).RequireAuthorization("Doctor");

        api.MapPost("/patients/{id:guid}/documents", async (
            Guid id,
            DocumentInput input,
            PracticeDb db,
            PracticeAccess a) =>
        {
            await a.Patient(id);

            Require(
                input.Kind is "Prescription" or "Certificate" or "Referral",
                "Invalid document kind.");

            Require(
                input.Kind == "Prescription"
                    ? input.Lines.Count > 0
                    : input.Lines.Count == 0 &&
                      !string.IsNullOrWhiteSpace(input.Body),
                "Check document content.");

            var row = new MedicalDocument
            {
                PatientId = id,
                DoctorId = a.UserId,
                Kind = input.Kind,
                Body = input.Body,

                Lines = input.Lines.Select((x, i) =>
                    new PrescriptionLine
                    {
                        Position = i,
                        Medicine = x.Medicine,
                        Instructions = x.Instructions,
                        Quantity = x.Quantity
                    }).ToList()
            };

            db.Documents.Add(row);
            await a.Save("document.draft", id);

            return Results.Ok(row);
        }).RequireAuthorization("Doctor");

        api.MapGet("/patients/{id:guid}/documents", async (
            Guid id,
            PracticeDb db,
            PracticeAccess a) =>
        {
            var p = await a.Patient(id);

            if (!a.Care && p.AccountId != a.UserId)
                throw new ApiError(404, "Patient not found.");

            var q = db.Documents
                .AsNoTracking()
                .Where(x => x.PatientId == id);

            if (!a.Care)
                q = q.Where(x => x.Released && x.SignedAt != null);

            var rows = await q
                .OrderByDescending(x => x.CreatedAt)
                .Take(100)
                .ToListAsync();

            await a.Save("documents.read", id);

            return Results.Ok(rows);
        });

        api.MapPost("/documents/{id:guid}/sign", async (
            Guid id,
            SignInput input,
            PracticeDb db,
            PracticeAccess a,
            UserManager<User> users,
            IConfiguration config) =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();

            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"Documents\" WHERE \"Id\" = {id} FOR UPDATE");

            var row = await db.Documents.Where(x => x.Id == id)
                .SingleOrDefaultAsync()
                ?? throw new ApiError(404, "Document not found.");

            Require(
                row.DoctorId == a.UserId && row.SignedAt is null,
                "Only the author can finalize a draft.");

            var p = await a.Patient(row.PatientId);
            var doctor = (await users.FindByIdAsync(a.UserId))!;

            row.PatientName = p.Name;
            row.BirthDate = p.BirthDate;

            row.DoctorName = string.IsNullOrWhiteSpace(doctor.DisplayName)
                ? doctor.UserName!
                : doctor.DisplayName;

            row.PracticeName = config["Practice:Name"]!;
            row.SignedAt = DateTimeOffset.UtcNow;
            row.Released = input.Release;

            if (input.Release)
                a.Notify(p.Id, "DocumentAvailable", row.Id);

            await a.Save("document.sign", p.Id);
            await tx.CommitAsync();

            return Results.Ok(row);
        }).RequireAuthorization("Doctor");

        api.MapPost("/documents/{id:guid}/cancel", async (
            Guid id,
            PracticeDb db,
            PracticeAccess a) =>
        {
            var row = await db.Documents.FindAsync(id)
                ?? throw new ApiError(404, "Document not found.");

            Require(
                row.DoctorId == a.UserId &&
                row.SignedAt is not null &&
                row.CancelledAt is null,
                "Only the author can cancel a finalized document.");

            row.CancelledAt = DateTimeOffset.UtcNow;
            row.Released = false;

            a.Notify(row.PatientId, "DocumentCancelled", row.Id);
            await a.Save("document.cancel", row.PatientId);

            return Results.NoContent();
        }).RequireAuthorization("Doctor");

        api.MapGet("/documents/{id:guid}/print", async (
            Guid id,
            PracticeDb db,
            PracticeAccess a) =>
        {
            var row = await db.Documents
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == id)
                ?? throw new ApiError(404, "Document not found.");

            var p = await a.Patient(row.PatientId);

            if (!a.Care &&
                (p.AccountId != a.UserId || !row.Released))
            {
                throw new ApiError(404, "Document not found.");
            }

            Require(
                row.SignedAt is not null && row.CancelledAt is null,
                "Only an active finalized document can be printed.");

            await a.Save("document.print", p.Id);

            string E(string? s) =>
                HtmlEncoder.Default.Encode(s ?? "");

            var lines = string.Join(
                "",
                row.Lines.OrderBy(x => x.Position).Select(x =>
                    $"<li><strong>{E(x.Medicine)}</strong>" +
                    $"<p>{E(x.Instructions)}</p>" +
                    $"<p>Quantity: {x.Quantity}</p></li>"));

            var html =
                "<!doctype html><html><meta charset='utf-8'>" +
                $"<title>{E(row.Kind)}</title><body>" +
                $"<h1>{E(row.PracticeName)}</h1>" +
                $"<h2>{E(row.Kind)}</h2>" +
                $"<p>Doctor: {E(row.DoctorName)}</p>" +
                $"<p>Patient: {E(row.PatientName)} — " +
                $"{row.BirthDate:yyyy-MM-dd}</p>" +
                $"<ol>{lines}</ol>" +
                $"<p>{E(row.Body).Replace("\n", "<br>")}</p>" +
                $"<p>Finalized: {row.SignedAt:O}</p>" +
                $"<p>Document: {row.Id}</p>" +
                "<p>Signature / stamp:</p></body></html>";

            return Results.Content(
                html,
                "text/html; charset=utf-8");
        });

        api.MapPost("/patients/{id:guid}/calculations/bmi", async (
            Guid id,
            BmiInput input,
            PracticeDb db,
            PracticeAccess a) =>
        {
            await a.Patient(id);

            var metres = input.HeightCm / 100m;
            var result = decimal.Round(
                input.WeightKg / (metres * metres),
                2);

            var row = new ClinicalEntry
            {
                PatientId = id,
                AuthorId = a.UserId,
                Kind = "Calculation",
                Title = "BMI",
                Value = result,
                Unit = "kg/m²",
                ObservedAt = DateTimeOffset.UtcNow,

                Content = System.Text.Json.JsonSerializer.Serialize(new
                {
                    formula = "kg / m²",
                    version = "1",
                    input.WeightKg,
                    input.HeightCm
                })
            };

            db.Entries.Add(row);
            await a.Save("calculation.bmi", id);

            return Results.Ok(new
            {
                formula = "BMI = kg / m²",
                version = "1",
                input.WeightKg,
                input.HeightCm,
                result,
                unit = "kg/m²"
            });
        }).RequireAuthorization("Care");
    }
}
