using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;

namespace Cardio.Domain;

public class User : IdentityUser
{
    public string DisplayName { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

public abstract class Entity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class Patient : Entity
{
    public string Name { get; set; } = "";
    public DateOnly BirthDate { get; set; }
    public string Phone { get; set; } = "";
    public string? AccountId { get; set; }
}

public class Slot : Entity
{
    public string DoctorId { get; set; } = "";
    public string? ResourceKey { get; set; }

    public DateTimeOffset Start { get; set; }
    public DateTimeOffset End { get; set; }

    public bool Closed { get; set; }
}

public class Appointment : Entity
{
    public Guid PatientId { get; set; }
    public Guid SlotId { get; set; }

    public DateTimeOffset Start { get; set; }
    public DateTimeOffset End { get; set; }

    public Guid RequestKey { get; set; }
    public string Status { get; set; } = "Confirmed";
}

public class ClinicalEntry : Entity
{
    public Guid PatientId { get; set; }
    public string AuthorId { get; set; } = "";

    public string Kind { get; set; } = "";
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";

    public decimal? Value { get; set; }
    public string? Unit { get; set; }
    public DateTimeOffset ObservedAt { get; set; }
}

public class Consultation : Entity
{
    public Guid PatientId { get; set; }
    public string DoctorId { get; set; } = "";

    public string Note { get; set; } = "";
    public Guid Version { get; set; } = Guid.NewGuid();
    public DateTimeOffset? SignedAt { get; set; }
}

public class MedicalDocument : Entity
{
    public Guid PatientId { get; set; }
    public string DoctorId { get; set; } = "";

    public string Kind { get; set; } = "";
    public string Body { get; set; } = "";

    public List<PrescriptionLine> Lines { get; set; } = [];

    // Identity snapshots are preserved when the document is finalized.
    public string PatientName { get; set; } = "";
    public DateOnly BirthDate { get; set; }
    public string DoctorName { get; set; } = "";
    public string PracticeName { get; set; } = "";

    public DateTimeOffset? SignedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public bool Released { get; set; }
}

public class PrescriptionLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Position { get; set; }

    public string Medicine { get; set; } = "";
    public string Instructions { get; set; } = "";
    public int Quantity { get; set; }
}

public class Invoice : Entity
{
    public Guid PatientId { get; set; }
    public string Description { get; set; } = "";
    public decimal Total { get; set; }
    public string Currency { get; set; } = "DZD";
}

public class Payment : Entity
{
    public Guid InvoiceId { get; set; }
    public decimal Amount { get; set; }
    public string Method { get; set; } = "";
    public Guid RequestKey { get; set; }
}

public class Message : Entity
{
    public Guid PatientId { get; set; }
    public string SenderId { get; set; } = "";
    public string Body { get; set; } = "";
}

public class Attachment : Entity
{
    public Guid PatientId { get; set; }
    public string Name { get; set; } = "";

    [JsonIgnore]
    public string StorageKey { get; set; } = "";

    public string Sha256 { get; set; } = "";
    public bool Released { get; set; }
}

public class Notification : Entity
{
    public Guid PatientId { get; set; }
    public string Kind { get; set; } = "";
    public Guid ReferenceId { get; set; }
}

public class AuditEvent : Entity
{
    public string ActorId { get; set; } = "";
    public Guid? PatientId { get; set; }
    public string Action { get; set; } = "";
}
