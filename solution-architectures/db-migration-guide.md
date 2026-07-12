# KinCare — Safe Database Migration Guide

## How migrations run in CI/CD

Migrations run as a dedicated `migrate-*` job in GitHub Actions **before** the new API
image is deployed to Railway. The currently-running API stays live and serves traffic while
the migration runs. This means every migration that ships must be safe to run against
the database while the **old** API version is still executing queries.

```
push to develop / main
        │
        ├── build-and-test   (unit + integration tests)
        ├── push-api-image   (Docker image → ghcr.io)
        │
        ▼
    migrate-prod             ← runs dotnet ef database update
        │                       old API keeps serving traffic
        ▼
    deploy-api-prod          ← new API image goes live on Railway
        │
        ▼
    e2e-smoke-prod           ← smoke tests run against new deployment
        │
        ├── pass → done
        └── fail → rollback-on-failure (railway rollback)
```

## The golden rule: every migration must be backward-compatible

The old API version must continue to work correctly **after** the migration runs and
**before** the new API is deployed. Migrations that break the running API cause downtime.

---

## Safe patterns

### Adding a column

Always add as **nullable** or with a **default value** so existing rows and the old API
code (which does not know about the column yet) continue to function.

```csharp
// GOOD — nullable column, old API ignores it
migrationBuilder.AddColumn<string>(
    name: "DriverNotes",
    table: "Residents",
    nullable: true);

// GOOD — column with default, old rows get the default
migrationBuilder.AddColumn<bool>(
    name: "IsVerified",
    table: "Vendors",
    defaultValue: false);
```

### Adding a table

Always safe — the old API does not reference it.

### Adding an index

Always safe — read-only change from the application's perspective.

### Adding a FK constraint (nullable)

Safe when nullable — existing rows with NULL satisfy the constraint.

---

## Patterns that require two migrations (two deploys)

### Renaming a column

A single rename breaks the running API which still references the old name.
Use two-phase migration over two separate deploys:

**Phase 1 (deploy N):**
```csharp
// Add the new column (nullable), copy data, keep old column
migrationBuilder.AddColumn<string>("PhoneE164", "Vendors", nullable: true);
migrationBuilder.Sql("UPDATE vendors SET phone_e164 = phone_number");
```

**Phase 2 (deploy N+1):** only after deploy N has been running successfully:
```csharp
// Drop the old column — new API no longer references it
migrationBuilder.DropColumn("PhoneNumber", "Vendors");
migrationBuilder.AlterColumn<string>("PhoneE164", "Vendors", nullable: false);
```

### Dropping a column

Same two-phase approach: stop referencing the column in code (deploy N), then drop it
in a migration (deploy N+1). Never drop a column that the running API still reads.

### Making a nullable column NOT NULL

```csharp
// Phase 1 — backfill NULLs, keep nullable
migrationBuilder.Sql("UPDATE rides SET notes = '' WHERE notes IS NULL");

// Phase 2 — enforce NOT NULL (separate deploy after Phase 1 is live)
migrationBuilder.AlterColumn<string>("Notes", "Rides", nullable: false, defaultValue: "");
```

### Adding a NOT NULL FK column to an existing table

Phase 1: add as nullable. Phase 2 (after code writes the value): make NOT NULL.

---

## Patterns that are always unsafe (never do these in a single deploy)

| Operation | Why it breaks |
|---|---|
| `DROP COLUMN` on a column the running API reads | API gets `column does not exist` error |
| `ALTER COLUMN` to NOT NULL with no default and existing NULL rows | Migration fails mid-run |
| `DROP TABLE` | Old API crashes on any query to that table |
| `RENAME TABLE` | Old API cannot find the table |
| Removing an enum value | Old API may produce the removed value, DB rejects it |

---

## Running migrations locally

```bash
# Apply all pending migrations
dotnet ef database update --project src/KinCare.API

# Check what's pending without applying
dotnet ef migrations list --project src/KinCare.API

# Check if model has un-scaffolded changes
dotnet ef migrations has-pending-model-changes --project src/KinCare.API

# Create a new migration
dotnet ef migrations add <MigrationName> \
  --project src/KinCare.API \
  --output-dir ../db/migrations

# Roll back one migration (local only — never do this against prod)
dotnet ef database update <PreviousMigrationName> --project src/KinCare.API
```

## Rolling back a production migration

EF Core does not support automatic rollback of applied migrations against a live DB.
If a migration must be undone:

1. Write a new **forward migration** that reverses the change (always additive).
2. Deploy that migration through the normal CI/CD pipeline.
3. Never run `dotnet ef database update <PreviousMigration>` against production —
   this requires the old codebase to be checked out locally and risks data loss.

The CI/CD pipeline's `rollback-on-failure` job only rolls back the **Railway deployment**
(the API binary), not the database schema. Schema rollback is always a new forward migration.

---

## GitHub secrets required

| Secret | Where | Description |
|---|---|---|
| `BETA_DB_CONNECTION_STRING` | beta environment | Neon/Postgres beta connection string |
| `PROD_DB_CONNECTION_STRING` | production environment | Neon/Postgres production connection string |

Connection string format:
```
Host=<host>;Database=kincare;Username=<user>;Password=<password>;SSL Mode=Require;Trust Server Certificate=true
```

---
Generated by Rocket Flow · 2.0.16 · 2026-07-01
