using Cardio.Domain;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Cardio.Data;

public class PracticeDb(DbContextOptions<PracticeDb> options)
    : IdentityDbContext<User>(options)
{
    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<Slot> Slots => Set<Slot>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<ClinicalEntry> Entries => Set<ClinicalEntry>();
    public DbSet<Consultation> Consultations => Set<Consultation>();
    public DbSet<MedicalDocument> Documents => Set<MedicalDocument>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.HasPostgresExtension("btree_gist");

        b.Entity<Patient>().HasIndex(x => x.AccountId).IsUnique();

        b.Entity<Patient>()
            .HasOne<User>().WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Entity<Slot>()
            .HasOne<User>().WithMany()
            .HasForeignKey(x => x.DoctorId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Entity<Slot>().HasIndex(x => x.Start);

        b.Entity<Slot>().ToTable(t =>
            t.HasCheckConstraint("slot_period", "\"End\" > \"Start\""));

        b.Entity<Appointment>()
            .HasOne<Slot>().WithMany()
            .HasForeignKey(x => x.SlotId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Entity<Appointment>()
            .HasIndex(x => x.SlotId)
            .IsUnique()
            .HasFilter("\"Status\" <> 'Cancelled'");

        b.Entity<Appointment>()
            .HasIndex(x => x.RequestKey)
            .IsUnique();

        b.Entity<Appointment>().ToTable(t =>
            t.HasCheckConstraint(
                "appointment_status",
                "\"Status\" IN ('Confirmed','CheckedIn','InProgress'," +
                "'Completed','Cancelled','NoShow')"));

        b.Entity<Appointment>().ToTable(t =>
            t.HasCheckConstraint("appointment_period", "\"End\" > \"Start\""));

        b.Entity<Consultation>()
            .Property(x => x.Version)
            .IsConcurrencyToken();

        b.Entity<MedicalDocument>().OwnsMany(x => x.Lines, p =>
        {
            p.ToTable("PrescriptionLines");
            p.WithOwner().HasForeignKey("DocumentId");
            p.HasKey(x => x.Id);
        });

        b.Entity<Invoice>()
            .Property(x => x.Total)
            .HasPrecision(14, 2);

        b.Entity<Invoice>().ToTable(t =>
            t.HasCheckConstraint("invoice_positive", "\"Total\" > 0"));

        b.Entity<Payment>()
            .Property(x => x.Amount)
            .HasPrecision(14, 2);

        b.Entity<Payment>()
            .HasIndex(x => x.RequestKey)
            .IsUnique();

        b.Entity<Payment>()
            .HasOne<Invoice>().WithMany()
            .HasForeignKey(x => x.InvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Entity<Payment>().ToTable(t =>
            t.HasCheckConstraint("payment_positive", "\"Amount\" > 0"));

        b.Entity<ClinicalEntry>()
            .Property(x => x.Value)
            .HasPrecision(18, 6);

        foreach (var type in new[]
        {
            typeof(Appointment),
            typeof(ClinicalEntry),
            typeof(Consultation),
            typeof(MedicalDocument),
            typeof(Invoice),
            typeof(Message),
            typeof(Attachment),
            typeof(Notification)
        })
        {
            b.Entity(type)
                .HasOne(typeof(Patient)).WithMany()
                .HasForeignKey("PatientId")
                .OnDelete(DeleteBehavior.Restrict);

            b.Entity(type).HasIndex("PatientId", "CreatedAt");
        }

        foreach (var type in new[]
        {
            typeof(ClinicalEntry),
            typeof(Consultation),
            typeof(MedicalDocument),
            typeof(Message)
        })
        {
            var key = type == typeof(ClinicalEntry)
                ? "AuthorId"
                : type == typeof(Message)
                    ? "SenderId"
                    : "DoctorId";

            b.Entity(type)
                .HasOne(typeof(User)).WithMany()
                .HasForeignKey(key)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
