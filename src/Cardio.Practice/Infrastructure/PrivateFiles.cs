using System.Security.Cryptography;
using System.Text;
using Cardio.Data;
using Cardio.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cardio.Infrastructure;

public class PrivateFiles(
    IWebHostEnvironment env,
    IConfiguration config)
{
    private string Root => Path.GetFullPath(
        config["Files:Root"] ??
        Path.Combine(env.ContentRootPath, "PrivateData"));

    public string Resolve(string key) =>
        Path.Combine(Root, key);

    public async Task<(string Key, string Hash)> Store(
        IFormFile file,
        CancellationToken ct)
    {
        if (!env.IsDevelopment())
        {
            throw new ApiError(
                503,
                "Production upload scanning is not configured.");
        }

        PracticeAccess.Require(
            file.Length is >= 5 and <= 10 * 1024 * 1024,
            "PDF limit: 10 MB.");

        Directory.CreateDirectory(Root);

        var key = Guid.NewGuid().ToString("N") + ".pdf";
        var path = Resolve(key);

        try
        {
            await using (var source = file.OpenReadStream())
            await using (var target = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write))
            {
                await source.CopyToAsync(target, ct);
            }

            await using var saved = File.OpenRead(path);

            var header = new byte[5];
            await saved.ReadExactlyAsync(header, ct);

            PracticeAccess.Require(
                Encoding.ASCII.GetString(header) == "%PDF-",
                "Only PDF attachments are accepted.");

            saved.Position = 0;

            return (
                key,
                Convert.ToHexString(
                    await SHA256.HashDataAsync(saved, ct)));
        }
        catch
        {
            File.Delete(path);
            throw;
        }
    }

    public static void MapFiles(RouteGroupBuilder api)
    {
        api.MapPost("/patients/{id:guid}/files", async (
            Guid id,
            HttpRequest request,
            PrivateFiles files,
            PracticeAccess a,
            PracticeDb db,
            CancellationToken ct) =>
        {
            await a.Patient(id);

            var form = await request.ReadFormAsync(ct);

            var file = form.Files.GetFile("file")
                ?? throw new ApiError(400, "Include a file field.");

            var stored = await files.Store(file, ct);

            var row = new Attachment
            {
                PatientId = id,
                StorageKey = stored.Key,
                Sha256 = stored.Hash,
                Name = "report.pdf"
            };

            try
            {
                db.Attachments.Add(row);
                await a.Save("attachment.upload", id);
            }
            catch
            {
                File.Delete(files.Resolve(stored.Key));
                throw;
            }

            return Results.Ok(row);
        }).RequireAuthorization("Care");

        api.MapGet("/patients/{id:guid}/files", async (
            Guid id,
            PracticeDb db,
            PracticeAccess a) =>
        {
            var p = await a.Patient(id);

            if (!a.Care && p.AccountId != a.UserId)
                throw new ApiError(404, "Patient not found.");

            var rows = await db.Attachments
                .AsNoTracking()
                .Where(x =>
                    x.PatientId == id &&
                    (a.Care || x.Released))
                .OrderByDescending(x => x.CreatedAt)
                .Take(100)
                .ToListAsync();

            await a.Save("attachments.read", id);

            return Results.Ok(rows);
        });

        api.MapPost("/files/{id:guid}/release", async (
            Guid id,
            PracticeDb db,
            PracticeAccess a) =>
        {
            var row = await db.Attachments.FindAsync(id)
                ?? throw new ApiError(404, "File not found.");

            row.Released = true;

            a.Notify(row.PatientId, "ReportAvailable", row.Id);
            await a.Save("attachment.release", row.PatientId);

            return Results.NoContent();
        }).RequireAuthorization("Doctor");

        api.MapGet("/files/{id:guid}", async (
            Guid id,
            PracticeDb db,
            PracticeAccess a,
            PrivateFiles files) =>
        {
            var row = await db.Attachments
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == id)
                ?? throw new ApiError(404, "File not found.");

            var p = await a.Patient(row.PatientId);

            if (!a.Care &&
                (p.AccountId != a.UserId || !row.Released))
            {
                throw new ApiError(404, "File not found.");
            }

            var path = files.Resolve(row.StorageKey);

            if (!File.Exists(path))
                throw new ApiError(404, "File unavailable.");

            await a.Save("attachment.download", row.PatientId);

            return Results.File(
                path,
                "application/octet-stream",
                row.Name);
        });
    }
}
