-- Run as postgres, connected to postgres, with Auto-commit ON.
-- Run ONLY if the cardio database does not already exist.
-- CREATE DATABASE must run outside a transaction.
CREATE DATABASE cardio OWNER cardio_migrator;
