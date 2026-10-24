using System.ComponentModel.DataAnnotations;

namespace Cardio.Contracts;

public interface IRequest { }

public record PatientInput(
    [property: Required, StringLength(160)] string Name,
    DateOnly BirthDate,
    [property: Required, StringLength(40)] string Phone) : IRequest;

public record LinkInput(
    [property: Required] string UserId,
    bool IdentityChecked) : IRequest;

public record StaffInput(
    [property: Required, EmailAddress] string Email,
    [property: Required, StringLength(160)] string Name,
    [property: Required, StringLength(128, MinimumLength = 12)] string Password,
    [property: Required] string Role) : IRequest;

public record SlotInput(
    DateTimeOffset Start,
    DateTimeOffset End,
    [property: StringLength(80)] string? ResourceKey) : IRequest;

public record BookingInput(
    Guid SlotId,
    Guid? PatientId,
    Guid RequestKey) : IRequest;

public record StatusInput(
    [property: Required] string Status) : IRequest;

public record EntryInput(
    [property: Required] string Kind,
    [property: Required, StringLength(160)] string Title,
    [property: Required, StringLength(8000)] string Content,
    decimal? Value,
    [property: StringLength(30)] string? Unit,
    DateTimeOffset ObservedAt) : IRequest;

public record NoteInput(
    [property: Required, StringLength(16000)] string Note,
    Guid Version) : IRequest;

public record RxInput(
    [property: Required, StringLength(160)] string Medicine,
    [property: Required, StringLength(1000)] string Instructions,
    [property: Range(1, 10000)] int Quantity) : IRequest;

public record DocumentInput(
    [property: Required] string Kind,
    [property: Required(AllowEmptyStrings = true), StringLength(8000)] string Body,
    [property: Required, MaxLength(30)] List<RxInput> Lines) : IRequest;

public record SignInput(bool Release) : IRequest;

public record InvoiceInput(
    [property: Required, StringLength(300)] string Description,
    [property: Range(typeof(decimal), "0.01", "100000000")] decimal Total,
    [property: Required, RegularExpression("^[A-Z]{3}$")] string Currency) : IRequest;

public record PaymentInput(
    [property: Range(typeof(decimal), "0.01", "100000000")] decimal Amount,
    [property: Required] string Method,
    Guid RequestKey) : IRequest;

public record MessageInput(
    [property: Required, StringLength(4000)] string Body) : IRequest;

public record BmiInput(
    [property: Range(1, 500)] decimal WeightKg,
    [property: Range(20, 250)] decimal HeightCm) : IRequest;
