BEGIN;
SET LOCAL search_path = public;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='slot_doctor_overlap' AND conrelid='"Slots"'::regclass) THEN
        ALTER TABLE "Slots" ADD CONSTRAINT slot_doctor_overlap
        EXCLUDE USING gist ("DoctorId" WITH =, tstzrange("Start", "End", '[)') WITH &&);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='slot_resource_overlap' AND conrelid='"Slots"'::regclass) THEN
        ALTER TABLE "Slots" ADD CONSTRAINT slot_resource_overlap
        EXCLUDE USING gist ("ResourceKey" WITH =, tstzrange("Start", "End", '[)') WITH &&)
        WHERE ("ResourceKey" IS NOT NULL);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='patient_appointment_overlap' AND conrelid='"Appointments"'::regclass) THEN
        ALTER TABLE "Appointments" ADD CONSTRAINT patient_appointment_overlap
        EXCLUDE USING gist ("PatientId" WITH =, tstzrange("Start", "End", '[)') WITH &&)
        WHERE ("Status" <> 'Cancelled');
    END IF;
END $$;

CREATE OR REPLACE FUNCTION protect_signed_document()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF OLD."SignedAt" IS NOT NULL
       AND
       (to_jsonb(NEW) - ARRAY['CancelledAt', 'Released'])
       IS DISTINCT FROM
       (to_jsonb(OLD) - ARRAY['CancelledAt', 'Released'])
    THEN
        RAISE EXCEPTION 'Finalized document content is immutable';
    END IF;

    RETURN NEW;
END
$$;

DROP TRIGGER IF EXISTS document_immutable ON "Documents";

CREATE TRIGGER document_immutable
BEFORE UPDATE ON "Documents"
FOR EACH ROW
EXECUTE FUNCTION protect_signed_document();

CREATE OR REPLACE FUNCTION protect_prescription_line()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    document_id uuid;
BEGIN
    IF TG_OP = 'INSERT' THEN
        document_id := NEW."DocumentId";
    ELSE
        document_id := OLD."DocumentId";
    END IF;

    PERFORM 1
    FROM "Documents"
    WHERE "Id" = document_id
    FOR UPDATE;

    IF EXISTS (
        SELECT 1
        FROM "Documents"
        WHERE "Id" = document_id
          AND "SignedAt" IS NOT NULL
    ) THEN
        RAISE EXCEPTION 'Finalized prescription lines are immutable';
    END IF;

    IF TG_OP = 'UPDATE'
       AND NEW."DocumentId" <> OLD."DocumentId"
    THEN
        RAISE EXCEPTION 'Cannot move a prescription line';
    END IF;

    IF TG_OP = 'DELETE' THEN
        RETURN OLD;
    END IF;

    RETURN NEW;
END
$$;

DROP TRIGGER IF EXISTS prescription_immutable ON "PrescriptionLines";

CREATE TRIGGER prescription_immutable
BEFORE INSERT OR UPDATE OR DELETE ON "PrescriptionLines"
FOR EACH ROW
EXECUTE FUNCTION protect_prescription_line();

CREATE OR REPLACE FUNCTION protect_signed_consultation()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF OLD."SignedAt" IS NOT NULL THEN
        RAISE EXCEPTION 'Finalized consultation is immutable';
    END IF;

    RETURN NEW;
END
$$;

DROP TRIGGER IF EXISTS consultation_immutable ON "Consultations";

CREATE TRIGGER consultation_immutable
BEFORE UPDATE ON "Consultations"
FOR EACH ROW
EXECUTE FUNCTION protect_signed_consultation();

REVOKE UPDATE, DELETE
ON "AuditEvents", "Entries", "Payments"
FROM cardio_app;

REVOKE INSERT, UPDATE, DELETE
ON "__EFMigrationsHistory"
FROM cardio_app;

REVOKE DELETE
ON "Documents", "PrescriptionLines", "Consultations", "Patients"
FROM cardio_app;

COMMIT;
