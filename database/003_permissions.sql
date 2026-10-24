-- Run as postgres while connected to cardio, before EF migrations.
-- This also supports an existing EMPTY cardio database owned by postgres.
CREATE EXTENSION IF NOT EXISTS btree_gist;
REVOKE CONNECT, TEMPORARY ON DATABASE cardio FROM PUBLIC;
GRANT CONNECT ON DATABASE cardio TO cardio_migrator, cardio_app;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE, CREATE ON SCHEMA public TO cardio_migrator;
GRANT USAGE ON SCHEMA public TO cardio_app;

-- Default privileges apply to tables created by this exact role.
ALTER DEFAULT PRIVILEGES FOR ROLE cardio_migrator IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO cardio_app;
ALTER DEFAULT PRIVILEGES FOR ROLE cardio_migrator IN SCHEMA public
    GRANT USAGE, SELECT ON SEQUENCES TO cardio_app;
