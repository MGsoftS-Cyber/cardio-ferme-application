using Cardio.Contracts;
using Cardio.Data;
using Cardio.Domain;
using Cardio.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Cardio.Infrastructure.PracticeAccess;

namespace Cardio.Endpoints;

public static class OfficeEndpoints
{
    public static void MapOffice(this RouteGroupBuilder api)
    {
        api.MapPost("/patients/{id:guid}/invoices", async (
            Guid id,
            InvoiceInput input,
            PracticeDb db,
            PracticeAccess a) =>
        {
            await a.Patient(id);

            Require(
                decimal.Round(input.Total, 2) == input.Total,
                "Use at most two decimal places.");

            var row = new Invoice
            {
                PatientId = id,
                Description = input.Description,
                Total = input.Total,
                Currency = input.Currency
            };

            db.Invoices.Add(row);
            await a.Save("invoice.create", id);

            return Results.Ok(row);
        }).RequireAuthorization("Office");

        api.MapGet("/patients/{id:guid}/invoices", async (
            Guid id,
            PracticeDb db,
            PracticeAccess a) =>
        {
            await a.Patient(id);

            var rows = await db.Invoices
                .AsNoTracking()
                .Where(x => x.PatientId == id)
                .OrderByDescending(x => x.CreatedAt)
                .Take(100)
                .Select(x => new
                {
                    x.Id,
                    x.Description,
                    x.Total,
                    x.Currency,

                    Paid = db.Payments
                        .Where(p => p.InvoiceId == x.Id)
                        .Sum(p => (decimal?)p.Amount) ?? 0m
                })
                .ToListAsync();

            await a.Save("invoices.read", id);

            return Results.Ok(rows);
        }).RequireAuthorization("OfficeOrPatient");

        api.MapPost("/invoices/{id:guid}/payments", async (
            Guid id,
            PaymentInput input,
            PracticeDb db,
            PracticeAccess a) =>
        {
            Require(
                input.RequestKey != Guid.Empty &&
                new[] { "Cash", "Card", "Transfer" }.Contains(input.Method),
                "Invalid payment method or request key.");

            Require(
                decimal.Round(input.Amount, 2) == input.Amount,
                "Use at most two decimal places.");

            await using var tx = await db.Database.BeginTransactionAsync();

            var invoice = await db.Invoices
                .FromSqlInterpolated(
                    $"SELECT * FROM \"Invoices\" WHERE \"Id\" = {id} FOR UPDATE")
                .SingleOrDefaultAsync()
                ?? throw new ApiError(404, "Invoice not found.");

            var old = await db.Payments
                .SingleOrDefaultAsync(x =>
                    x.RequestKey == input.RequestKey);

            if (old is not null)
            {
                if (old.InvoiceId != id ||
                    old.Amount != input.Amount ||
                    old.Method != input.Method)
                {
                    throw new ApiError(
                        409,
                        "Request key already used for another payment.");
                }

                return Results.Ok(old);
            }

            var paid = await db.Payments
                .Where(x => x.InvoiceId == id)
                .SumAsync(x => (decimal?)x.Amount) ?? 0m;

            Require(
                paid + input.Amount <= invoice.Total,
                "Payment exceeds the remaining balance.");

            var row = new Payment
            {
                InvoiceId = id,
                Amount = input.Amount,
                Method = input.Method,
                RequestKey = input.RequestKey
            };

            db.Payments.Add(row);

            await a.Save("payment.record", invoice.PatientId);
            await tx.CommitAsync();

            return Results.Ok(row);
        }).RequireAuthorization("Office");

        api.MapGet("/patients/{id:guid}/messages", async (
            Guid id,
            PracticeDb db,
            PracticeAccess a) =>
        {
            await a.Patient(id);

            var rows = await db.Messages
                .AsNoTracking()
                .Where(x => x.PatientId == id)
                .OrderByDescending(x => x.CreatedAt)
                .Take(100)
                .ToListAsync();

            await a.Save("administrative.messages.read", id);

            return Results.Ok(rows);
        });

        api.MapPost("/patients/{id:guid}/messages", async (
            Guid id,
            MessageInput input,
            PracticeDb db,
            PracticeAccess a) =>
        {
            await a.Patient(id);

            var row = new Message
            {
                PatientId = id,
                SenderId = a.UserId,
                Body = input.Body
            };

            db.Messages.Add(row);
            await a.Save("administrative.message.create", id);

            return Results.Ok(row);
        });
    }
}
