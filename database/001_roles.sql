-- Run as postgres, connected to the postgres database.
-- These are the local development passwords requested for this package.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'cardio_migrator') THEN
        CREATE ROLE cardio_migrator LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'cardio_app') THEN
        CREATE ROLE cardio_app LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE;
    END IF;
END $$;

ALTER ROLE cardio_migrator PASSWORD 'M123456';
ALTER ROLE cardio_app PASSWORD 'AP123456';
