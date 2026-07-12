# KinCare — Claude Development Guide

## Project Overview

KinCare is a multi-tenant B2B SaaS platform for senior living facility coordinators to manage resident transport.
It replaces manual phone-based NEMT coordination with a mobile-first real-time dashboard.
Multiple facility clients (tenants) are served from a single deployment, each fully isolated.

**Repo root:** `~/Downloads/Projects/KinCare/`

## Stack

| Layer | Technology |
|---|---|
| Frontend | Angular 17 (standalone components), Angular Material, SCSS |
| Backend | .NET 9 Minimal API |
| Auth | ASP.NET Core Identity + JWT Bearer |
| ORM | Entity Framework Core 9 + Npgsql |
| Database | PostgreSQL 16 (local) |
| Background Jobs | Hangfire + Hangfire.PostgreSql |
| SMS | Twilio .NET SDK |
| Push Notifications | Firebase Admin .NET SDK (FCM) + Angular PWA service worker |
| Validation | FluentValidation |
| Real-time | SignalR (ASP.NET Core built-in) |
| Payments | Stripe .NET SDK |
| Ambulatory Dispatch | Uber Health API |
| NEMT Broker Fallback | Roundtrip Health API |

## Repo Structure

```
KinCare/
├── KinCare.sln
├── CLAUDE.md                        ← you are here
├── .gitignore
├── db/
│   ├── migrations/                  EF Core auto-generated migrations
│   ├── seeds/dev_seed.sql           local dev seed data
│   └── rls/                         PostgreSQL RLS policy scripts
├── src/
│   ├── KinCare.API/                 .NET 9 Minimal API
│   │   ├── Domain/                  entity classes + enums
│   │   ├── Data/                    EF Core DbContext
│   │   ├── Services/                business logic
│   │   │   └── Dispatch/            channel-specific dispatch services
│   │   ├── Endpoints/               Minimal API route handlers
│   │   ├── Jobs/                    Hangfire background jobs
│   │   ├── Hubs/                    SignalR hubs
│   │   ├── Webhooks/                inbound webhook handlers (Twilio, Stripe, Uber Health)
│   │   └── Infrastructure/          config classes (JWT, Twilio, FCM, Stripe, UberHealth)
│   ├── KinCare.Tests/               xUnit tests
│   └── KinCare.Web/                 Angular 17 PWA
│       └── src/app/
│           ├── dashboard/
│           ├── booking/
│           ├── ride-detail/
│           ├── residents/
│           ├── vendors/
│           ├── history/
│           └── shared/
│               ├── auth/
│               ├── models/
│               └── interceptors/
└── solution-architectures/          architecture reference docs
```

## How to Run

```bash
# Terminal 1 — API
dotnet run --project src/KinCare.API

# Terminal 2 — Angular
cd src/KinCare.Web && npm install && ng serve

# Terminal 3 — ngrok (for Twilio webhook testing)
ngrok http 5000
```

## Environment Setup

Copy `src/KinCare.API/appsettings.Development.json.example` to
`src/KinCare.API/appsettings.Development.json` and fill in values.
Never commit `appsettings.Development.json` or `firebase-service-account.json`.

## Tenant Hierarchy (Always Understand Before Writing Any Code)

```
Organization  (paying B2B customer — e.g. "Sunrise Senior Group LLC")
  └── Facility  (one or more physical locations under the org)
        └── AppUser  (coordinators scoped to one facility)
        └── Resident
        └── Vendor
        └── Ride
```

**Roles:**
- `SuperAdmin` — KinCare platform staff, cross-org visibility
- `OrgAdmin` — client's admin, manages all their facilities, views billing
- `Coordinator` — day-to-day user, scoped to one facility only

**Plan tiers** (stored on `Organization.PlanTier`):
- `Starter` — SMS dispatch, escalation, ride history
- `Professional` — + Uber Health, smart vendor GPS tracking, CSV export
- `Enterprise` — + multi-facility org dashboard, API access, SSO

**Feature gating:** plan checks happen in API middleware only — never trust Angular to gate features.

## Dispatch Channels (Always Route Through DispatchRouter)

Every ride is dispatched through exactly one channel. Channel is chosen automatically by `DispatchRouter` based on resident needs and org plan. Never call a channel service directly from an endpoint.

| Channel | Vendor type | How it works | Who triggers status updates |
|---|---|---|---|
| `SmsNemt` | Wheelchair / oxygen / stretcher | Twilio SMS, numbered replies (1–6) | Vendor SMS reply → Twilio webhook |
| `SmsTaxi` | Ambulatory (local taxi) | Twilio SMS, numbered replies (1–6) | Vendor SMS reply → Twilio webhook |
| `UberHealth` | Ambulatory (Professional plan only) | Uber Health API, no SMS | Uber Health webhook → status sync |
| `Broker` | Any (fallback when no local vendor) | Roundtrip Health API | Broker webhook → status sync |

**Routing logic in `DispatchRouter.RouteAsync`:**
```
if resident.NeedsWheelchair || resident.NeedsOxygen || resident.NeedsStretcher
  → SmsNemt (always — Uber Health and taxis cannot serve these)
else if org.PlanTier >= Professional AND facility.UberHealthEnabled
  → UberHealth
else if vendor.DispatchMethod == SmsTaxi
  → SmsTaxi
else
  → SmsNemt (fallback)
// If no local vendor available and org has Broker enabled → Broker
```

**`SmsTaxi` vs `SmsNemt`:** Both use identical Twilio SMS dispatch and numbered reply flow. The only differences are: taxi vendors have `VendorType = Ambulatory`, they never receive wheelchair/oxygen resident rides, and their booking SMS omits special needs tags. Same code path — same `TwilioService`.

## Coding Rules

- Minimal API only — no MVC controllers
- Standalone Angular components only — no NgModules
- JWT claims must include `organization_id`, `facility_id`, `role` — all three always present
- `TenantMiddleware` runs on every authenticated request — validates `Organization.IsActive`, attaches org+facility to request context
- All API handlers scope queries using both `organization_id` (for OrgAdmin) and `facility_id` (for Coordinator) from JWT claims — never trust request body for tenant identity
- Ride status transitions must go through `RideStateMachine` — never set `ride.Status` directly
- All dispatch must go through `DispatchRouter` — never call `TwilioService`, `UberHealthService`, or `BrokerService` directly from endpoints
- `RideEvent` records are append-only — never update or delete them
- All EF Core queries use the global query filter on `facility_id` — never bypass it
- Plan feature checks use `IPlanGate.Requires(org, PlanFeature.X)` — never hardcode plan tier strings in business logic
- FluentValidation for all request models — no inline validation in endpoints
- No secrets in code or committed config files

## Security Rules (Always Enforce)

- **Twilio webhook:** validate `X-Twilio-Signature` on every inbound request — return 403 on failure, log attempt
- **Twilio idempotency:** store processed `MessageSid` values — reject duplicates to prevent double status transitions
- **Twilio IP allowlist:** only accept webhook POSTs from Twilio's published IP ranges (configure at reverse proxy or middleware)
- **Hangfire dashboard:** never expose `/hangfire` without authentication in any environment — use `LocalRequestsOnlyAuthorizationFilter` in development, role-based auth in production
- **Tracking page token:** token expires immediately when ride reaches `Completed` or `Cancelled` — set `tracking_token = null` on those transitions
- **Rate limiting:** `POST /api/auth/login` — max 5 attempts per minute per IP; `POST /webhook/twilio` — max 60 requests per minute per IP
- **Input length limits:** enforce `MaxLength` on all string entity columns in EF Core — destination (500), address (500), driver notes (1000), vendor name (200)
- **appsettings.Development.json:** run `git rm --cached src/KinCare.API/appsettings.Development.json` before first commit — it is created by `dotnet new` before `.gitignore` takes effect
- **Stack traces:** global exception handler must never expose stack traces in API responses — use RFC 7807 problem details with generic messages in production

## Efficiency Rules (Always Enforce)

- **Real-time updates:** use SignalR (`Microsoft.AspNetCore.SignalR`) for ride status updates to Angular — no polling. Hub: `RideStatusHub`. Broadcast on every `RideService.AdvanceStatusAsync` call. Angular subscribes on dashboard load and disconnects on navigate away.
- **DB indexes:** the following indexes must exist from Feature 1 migration and must never be removed:
  - `facilities(organization_id)` — org-level queries
  - `rides(facility_id, pickup_time)` — dashboard and escalation queries
  - `rides(dispatch_channel, status)` — channel-specific status queries
  - `rides(tracking_token)` — tracking page lookup (partial index WHERE tracking_token IS NOT NULL)
  - `rides(external_trip_id)` — Uber Health / broker webhook lookup (partial index WHERE external_trip_id IS NOT NULL)
  - `vendors(phone_number)` — Twilio webhook vendor lookup
  - `ride_events(ride_id, occurred_at)` — ride detail timeline
  - `ride_events(ride_id, triggered_by)` — escalation idempotency check
- **EF Core projections:** `GET /api/rides` (dashboard) must use `.Select()` projection — never load full entity graph for list endpoints
- **Hangfire query scope:** escalation job queries must use the indexed columns above — always filter by `status` and `pickup_time` together, never scan all rides

## Build & Test Commands

```bash
dotnet build KinCare.sln          # must pass before every commit
dotnet test src/KinCare.Tests     # run after every feature
```

---

## Build Plan — Feature by Feature

Each feature is a complete vertical slice: domain → service → API → Angular.
Complete features in order. Do not start Feature N+1 until Feature N builds and tests pass.

---

### Feature 0 — Multi-Tenant Foundation & Onboarding
**Goal:** Organization and Facility hierarchy exists. New client can self-register. Coordinator invitation email works.

**Backend tasks:**
- [ ] Define tenant entities in `Domain/`:
  - `Organization`: `id`, `name`, `plan_tier` (enum: Starter/Professional/Enterprise), `is_active`, `stripe_customer_id`, `billing_email`, `created_at`
  - `Facility`: `id`, `organization_id` (FK), `name`, `address`, `timezone`, `uber_health_enabled`, `is_active`
  - `Invitation`: `id`, `organization_id`, `facility_id` (nullable), `email`, `role`, `token` (UUID), `expires_at`, `accepted_at`
- [ ] Define `UserRole` enum: `SuperAdmin, OrgAdmin, Coordinator`
- [ ] Define `PlanTier` enum: `Starter, Professional, Enterprise`
- [ ] Add `organization_id`, `facility_id` (nullable), `role` to `AppUser`
- [ ] Implement `IPlanGate` interface + `PlanGate` service — `Requires(org, PlanFeature)` throws 402 if plan insufficient
- [ ] Define `PlanFeature` enum: `UberHealthDispatch, SmartVendorTracking, CsvExport, OrgDashboard, BrokerDispatch`
- [ ] Implement `TenantMiddleware` — reads JWT claims, loads org from DB, checks `is_active` (return 402 if false), attaches `TenantContext` to request
- [ ] Implement `Endpoints/OnboardingEndpoints.cs`:
  - `POST /api/onboarding/register` — create Organization + first Facility + OrgAdmin account, trigger Stripe customer creation (placeholder until Feature 12)
  - `POST /api/onboarding/invite` — OrgAdmin sends invite, creates `Invitation` record, sends email via SendGrid
  - `GET /api/onboarding/invite/{token}` — validate token, return invite details
  - `POST /api/onboarding/accept` — coordinator sets password, creates account, marks invitation accepted

**Angular tasks:**
- [ ] Registration page — org name, facility name, admin email/password
- [ ] Accept invitation page — token from email URL, set password form
- [ ] Role-based route guards: `OrgAdminGuard`, `CoordinatorGuard`
- [ ] `shared/models/organization.model.ts`, `invitation.model.ts`

**Verify:**
- Register new org → Organization + Facility + OrgAdmin created in DB
- OrgAdmin invites coordinator → email received, invitation token in link
- Coordinator accepts invite → account created, can log in, facility-scoped JWT issued

---

### Feature 1 — Foundation & Auth
**Goal:** PostgreSQL connected, full schema created, all roles can log in and receive correctly-scoped JWTs.

**Backend tasks:**
- [ ] Define all core domain entities in `Domain/`:
  - `Resident`: `facility_id`, `first_name`, `last_name`, `needs_wheelchair`, `needs_oxygen`, `needs_stretcher`, `needs_walker`, `driver_notes`, `is_active`
  - `Vendor`: `facility_id`, `name`, `phone_number`, `vendor_type` (enum: Wheelchair/Ambulatory), `dispatch_method` (enum: SmsNemt/SmsTaxi/UberHealth/Broker), `capability_tier` (enum: Basic/Smart), `is_active`
  - `Ride`: `facility_id`, `organization_id`, `resident_id`, `vendor_id`, `status`, `dispatch_channel`, `external_trip_id` (nullable — for Uber/Broker), `pickup_time`, `pickup_address`, `destination_address`, `tracking_token`, `last_known_lat`, `last_known_lng`, `last_location_at`, `created_at`
  - `RideEvent`: `ride_id`, `from_status`, `to_status`, `triggered_by`, `notes`, `occurred_at`
- [ ] Define all enums: `RideStatus` (Dispatched/Confirmed/EnRoute/Arrived/Dropped/Completed/Cancelled), `DispatchChannel` (SmsNemt/SmsTaxi/UberHealth/Broker), `VendorType`, `VendorCapabilityTier`, `DispatchMethod`
- [ ] Create `AppDbContext` in `Data/` — configure Identity, all entities, global query filters on `facility_id` for Coordinator role, `organization_id` for OrgAdmin role
- [ ] In `AppDbContext.OnModelCreating` configure all indexes:
  - `facilities(organization_id)`
  - `rides(facility_id, pickup_time)`
  - `rides(dispatch_channel, status)`
  - `rides(tracking_token)` — partial WHERE NOT NULL
  - `rides(external_trip_id)` — partial WHERE NOT NULL
  - `vendors(phone_number)`
  - `ride_events(ride_id, occurred_at)`
  - `ride_events(ride_id, triggered_by)`
- [ ] Add EF Core migration: `dotnet ef migrations add InitialSchema --project src/KinCare.API --output-dir ../db/migrations`
- [ ] Apply migration: `dotnet ef database update --project src/KinCare.API`
- [ ] Run `git rm --cached src/KinCare.API/appsettings.Development.json`
- [ ] Implement `Infrastructure/JwtConfig.cs` — JWT includes `organization_id`, `facility_id`, `role` claims
- [ ] Implement `Endpoints/AuthEndpoints.cs`:
  - `POST /api/auth/login` — return JWT + HttpOnly refresh cookie
  - `POST /api/auth/refresh` — refresh token rotation
  - `POST /api/auth/logout` — clear cookie
- [ ] Register in `Program.cs`: DbContext, Identity, JWT Bearer, TenantMiddleware, CORS, RateLimiter

**Angular tasks:**
- [ ] `shared/auth/auth.service.ts` — login(), logout(), refreshToken(), currentUser$, hasRole()
- [ ] `shared/interceptors/jwt.interceptor.ts` — attach Bearer token
- [ ] `shared/auth/auth.guard.ts`, `org-admin.guard.ts`
- [ ] Login page component
- [ ] `app.routes.ts` with role-based guards

**Verify:**
- `dotnet build` passes with zero warnings
- Coordinator login → JWT contains `facility_id` + `role: Coordinator`
- OrgAdmin login → JWT contains `organization_id` + `role: OrgAdmin`, no `facility_id`
- Inactive org → all API requests return 402

---

### Feature 2 — Residents & Vendors
**Goal:** Coordinator can manage resident profiles and vendor records. Vendor dispatch method is selectable.

**Backend tasks:**
- [ ] `Endpoints/ResidentEndpoints.cs`:
  - `GET /api/residents` — facility-scoped list, active only
  - `POST /api/residents` — create with special needs flags
  - `PUT /api/residents/{id}` — update
  - `DELETE /api/residents/{id}` — soft delete
- [ ] `Endpoints/VendorEndpoints.cs`:
  - `GET /api/vendors` — facility-scoped, supports `?type=wheelchair&type=ambulatory` filter
  - `POST /api/vendors` — create with `dispatch_method` (SmsNemt/SmsTaxi/UberHealth/Broker)
  - `PUT /api/vendors/{id}` — update
  - `DELETE /api/vendors/{id}` — soft delete
- [ ] FluentValidation for all request models
- [ ] Vendor with `dispatch_method: UberHealth` — require Professional plan via `IPlanGate`

**Angular tasks:**
- [ ] `residents/residents.component` — list + add/edit sheet, special needs checkboxes
- [ ] `vendors/vendors.component` — list + add/edit, dispatch method selector, capability tier badge
- [ ] `shared/models/resident.model.ts`, `vendor.model.ts`
- [ ] Routes: `/residents`, `/vendors`

**Verify:**
- Create wheelchair resident → appears with correct flags
- Create SmsTaxi vendor → appears with taxi badge
- Starter plan org creates UberHealth vendor → 402 returned

---

### Feature 3 — Ride Booking, Dashboard & SignalR
**Goal:** Coordinator books rides. Dashboard shows today's rides. Status updates are real-time via SignalR.

**Backend tasks:**
- [ ] `Services/RideStateMachine.cs` — strict transition map, `CanTransition`, `Transition` (appends RideEvent, nulls tracking token on terminal states)
- [ ] `Services/Dispatch/DispatchRouter.cs` — routes to correct channel based on resident needs + org plan:
  - `SmsNemt` / `SmsTaxi` → `TwilioDispatchService` (placeholder, implemented in Feature 4)
  - `UberHealth` → `UberHealthDispatchService` (placeholder, implemented in Feature 11)
  - `Broker` → `BrokerDispatchService` (placeholder, implemented in Feature 11)
- [ ] `Services/RideService.cs`:
  - `BookRideAsync` — create ride, call `DispatchRouter.RouteAsync`, set `dispatch_channel` on ride record
  - `GetTodaysRidesAsync` — projection query, indexed columns only
  - `AdvanceStatusAsync` — state machine → DB → SignalR broadcast → FCM (placeholder)
  - `GetRideDetailAsync` — ride + ordered ride events
- [ ] `Endpoints/RideEndpoints.cs`: GET today, POST book, GET detail, PUT status, DELETE cancel
- [ ] `Hubs/RideStatusHub.cs` — coordinator joins group `facility:{facility_id}` on connect, JWT via query string
- [ ] Register SignalR in `Program.cs`, broadcast `RideStatusChanged` from `AdvanceStatusAsync`

**Angular tasks:**
- [ ] `dashboard/dashboard.component` — ride cards, status badge colour map, channel icon (SMS/Uber/Taxi/Broker)
- [ ] SignalR connection on dashboard init (`@microsoft/signalr`), update card in-place on `RideStatusChanged`
- [ ] `booking/booking.component` — bottom sheet, resident dropdown, vendor dropdown filtered by dispatch compatibility, date/time, destination
- [ ] FAB opens booking sheet
- [ ] `ride-detail/ride-detail.component` — full detail + event timeline + status buttons
- [ ] `shared/models/ride.model.ts`

**Verify:**
- Book ride → `dispatch_channel` set correctly based on resident needs
- Wheelchair resident → only SmsNemt/Broker vendors shown in dropdown
- Advance status → dashboard card updates instantly via SignalR

---

### Feature 4 — SMS Dispatch (NEMT & Taxi)
**Goal:** SmsNemt and SmsTaxi rides fire structured SMS to vendor. Both channels use identical Twilio flow.

**Backend tasks:**
- [ ] `Infrastructure/TwilioConfig.cs`
- [ ] `Services/Dispatch/TwilioDispatchService.cs`:
  - `SendBookingSmsAsync(ride, vendor, resident)` — single method serves both SmsNemt and SmsTaxi
  - SMS includes: resident first name, special needs tags (omitted for SmsTaxi ambulatory rides), pickup address, dropoff address, pickup time, tracking URL if smart vendor, "Reply 1 ACCEPT / 2 DECLINE"
  - `SendCheckpointSmsAsync(ride, vendor, checkpoint)` — On My Way prompt, Arrived prompt, etc.
- [ ] Wire `TwilioDispatchService` into `DispatchRouter` for SmsNemt and SmsTaxi channels
- [ ] Fire-and-forget with error logging — never block ride creation on SMS failure

**Verify:**
- SmsNemt ride → SMS with wheelchair special needs tag received
- SmsTaxi ride → SMS without special needs tag received, same numbered reply instructions
- Bad phone number → ride created, error logged, no 500

---

### Feature 5 — Twilio Inbound Webhook
**Goal:** Vendor numbered replies (1–6) auto-advance ride status for both SmsNemt and SmsTaxi channels.

**Backend tasks:**
- [ ] `Webhooks/TwilioWebhookHandler.cs`:
  - Validate `X-Twilio-Signature` → 403 on failure
  - Idempotency: check `ride_events.notes` for `twilio_sid:{MessageSid}` → 200 if already processed
  - Lookup: find active ride by vendor `phone_number` (covers both SmsNemt and SmsTaxi)
  - Parse first digit → map to transition (1=Confirmed, 2=Cancelled, 3=EnRoute, 4=Arrived, 5=Dropped, 6=Issue)
  - Call `RideService.AdvanceStatusAsync`, store MessageSid in RideEvent.Notes
  - Return empty TwiML 200
- [ ] Register `/webhook/twilio` — exclude from JWT and TenantMiddleware

**Verify:**
- SmsTaxi vendor replies `1` → Confirmed
- SmsNemt vendor replies `4` → Arrived + FCM placeholder fires
- Same MessageSid sent twice → processed once only

---

### Feature 6 — Escalation & Hangfire
**Goal:** Coordinator alerted when any SMS-dispatched driver goes silent. Uber/Broker rides excluded (managed by their platforms).

**Backend tasks:**
- [ ] Configure Hangfire with PostgreSQL storage, embedded server, auth-protected dashboard
- [ ] `Jobs/EscalationJob.cs` — recurring every 5 minutes:
  - Only query rides WHERE `dispatch_channel IN (SmsNemt, SmsTaxi)` — never escalate Uber/Broker rides
  - Dispatched + 30min past pickup → "No confirmation" alert
  - Confirmed + 15min before pickup → "Hasn't departed" alert
  - EnRoute + 45min past pickup → "May be delayed" alert
  - Arrived + 20min → "May need boarding help" alert
  - Idempotency: check ride_events for prior escalation of same type before firing
  - Log escalation as RideEvent `triggered_by: escalation_job`
- [ ] `Jobs/CheckpointReminderJob.cs` — per-ride scheduled jobs on status advance

**Verify:**
- Uber Health ride left in EnRoute → no escalation fired (dispatch_channel filter works)
- SmsTaxi ride silent past threshold → escalation RideEvent logged

---

### Feature 7 — FCM Push Notifications
**Goal:** Real push notifications to coordinator for all channels — arrivals, drops, escalations, issues.

**Backend tasks:**
- [ ] `Infrastructure/FcmConfig.cs`, `Services/FcmService.cs`
- [ ] `Endpoints/DeviceEndpoints.cs` — `POST /api/devices/register` saves FCM token
- [ ] Replace all FCM placeholders in `RideService` and `EscalationJob`:
  - Arrived → "🚐 [Vendor/Uber] is outside for [Resident]"
  - Dropped → "✅ [Resident] safely at [Destination]"
  - Escalation → "⚠️ [message]"
  - Reply 6 (issue) → "🚨 [Vendor] reported issue for [Resident]"
  - Uber/Broker status sync → same push triggers (Feature 11 wires these)

**Angular tasks:**
- [ ] `ng add @angular/pwa` (off corporate network), Firebase JS SDK, service worker FCM registration
- [ ] Request notification permission on first login
- [ ] POST FCM token to `/api/devices/register`

**Verify:**
- Real device, ride Arrived → push within 3 seconds
- Escalation fires → push received while app backgrounded

---

### Feature 8 — Smart Vendor GPS Tracking
**Goal:** Smart-tier SMS vendors get tokenized tracking page with one-tap status buttons and optional GPS.

**Backend tasks:**
- [ ] `Ride` entity already has tracking columns from Feature 1 schema
- [ ] Generate tracking token in `TwilioDispatchService.SendBookingSmsAsync` for Smart vendors
- [ ] `RideStateMachine.Transition` nulls `TrackingToken` on Completed/Cancelled
- [ ] `GET /track/{token}` — lightweight Razor page, public, shows ride details + one-tap buttons + Google Maps deeplinks
- [ ] `POST /api/rides/location` — token auth, update last known lat/lng

**Angular tasks:**
- [ ] Dashboard ride cards: 📍 indicator if `last_location_at` < 10min ago
- [ ] Ride detail: Google Maps embed with driver pin for smart vendor rides

**Verify:**
- Smart NEMT vendor booking SMS contains tracking URL
- SmsTaxi Basic vendor booking SMS has no tracking URL
- GPS coordinates update on ride record every 30 sec

---

### Feature 9 — Ride History & CSV Export
**Goal:** Coordinator sees full history. OrgAdmin sees history across all facilities. CSV export for compliance.

**Backend tasks:**
- [ ] `GET /api/rides/history` — Coordinator: facility-scoped. OrgAdmin: all facilities in org. Paginated, date range + status + channel filters
- [ ] `GET /api/rides/history/export` — CSV download, requires Professional plan via `IPlanGate`
- [ ] CSV columns: Date, Channel, Resident Name, Vendor, Pickup, Destination, all status timestamps

**Angular tasks:**
- [ ] `history/history.component` — paginated table, filters, export button (hidden on Starter plan)
- [ ] OrgAdmin view shows facility column
- [ ] `/history` route

**Verify:**
- Coordinator export on Starter plan → 402 returned
- OrgAdmin sees rides from all their facilities
- CSV opens correctly in Excel with all timestamps

---

### Feature 10 — Org Admin Dashboard
**Goal:** OrgAdmin can manage all facilities, coordinators, and view consolidated metrics.

**Backend tasks:**
- [ ] `Endpoints/OrgAdminEndpoints.cs`:
  - `GET /api/org/facilities` — list all facilities in org
  - `POST /api/org/facilities` — create new facility
  - `GET /api/org/users` — list all coordinators across org
  - `POST /api/org/invite` — invite new coordinator to a facility
  - `DELETE /api/org/users/{id}` — deactivate coordinator
  - `GET /api/org/metrics` — ride counts, completion rates per facility (last 30 days)
- [ ] All endpoints require `OrgAdmin` role — return 403 for Coordinators

**Angular tasks:**
- [ ] `/org` route — OrgAdmin-only section
- [ ] Facilities list with per-facility ride count
- [ ] Coordinator management table with invite button
- [ ] Metrics summary: rides this month, on-time rate, top vendor per facility

**Verify:**
- Coordinator accessing `/api/org/*` → 403
- OrgAdmin sees all facilities and coordinators
- Metrics reflect actual completed rides

---

### Feature 11 — Uber Health & Broker Dispatch
**Goal:** Professional plan orgs can dispatch ambulatory rides via Uber Health. Broker fallback when no local vendor.

**Backend tasks:**
- [ ] `Infrastructure/UberHealthConfig.cs`, `Infrastructure/BrokerConfig.cs`
- [ ] `Services/Dispatch/UberHealthDispatchService.cs`:
  - `BookRideAsync` — call Uber Health API, store `external_trip_id` on ride
  - `CancelRideAsync` — cancel via Uber Health API
  - `SyncStatusAsync(externalTripId)` — map Uber status → KinCare RideStatus
- [ ] `Services/Dispatch/BrokerDispatchService.cs` — same interface, Roundtrip Health API
- [ ] `Webhooks/UberHealthWebhookHandler.cs` — `POST /webhook/uber-health`, validate signature, call `RideService.AdvanceStatusAsync`
- [ ] `Webhooks/BrokerWebhookHandler.cs` — `POST /webhook/broker`, similar pattern
- [ ] Wire both services into `DispatchRouter` — replace placeholders from Feature 3
- [ ] `Jobs/ExternalTripSyncJob.cs` — poll Uber Health / Broker every 2 min for rides where `external_trip_id IS NOT NULL` and status not terminal (fallback if webhook missed)
- [ ] Plan gate: UberHealth and Broker require `PlanFeature.UberHealthDispatch` / `PlanFeature.BrokerDispatch`

**Verify:**
- Starter plan org books ambulatory ride → routes to SmsTaxi, not UberHealth (plan gate works)
- Professional plan org books ambulatory ride with UberHealth vendor → Uber Health API called, `external_trip_id` stored
- Uber Health webhook fires → ride status updates, SignalR broadcasts to dashboard
- No escalation job fires for Uber Health rides

---

### Feature 12 — Billing (Stripe)
**Goal:** Organizations subscribe to a plan. Payment failure disables access. Usage is metered.

**Backend tasks:**
- [ ] `Infrastructure/StripeConfig.cs`, add Stripe.net NuGet package
- [ ] On org registration (`Feature 0`): create Stripe Customer, store `stripe_customer_id`
- [ ] `Endpoints/BillingEndpoints.cs`:
  - `POST /api/billing/subscribe` — create Stripe subscription for selected plan, store `stripe_subscription_id`
  - `GET /api/billing/portal` — return Stripe Customer Portal URL for self-service billing management
  - `GET /api/billing/plan` — return current plan and usage
- [ ] `Webhooks/StripeWebhookHandler.cs` — `POST /webhook/stripe`, validate Stripe signature:
  - `invoice.paid` → ensure `Organization.IsActive = true`
  - `invoice.payment_failed` → set `Organization.IsActive = false` (TenantMiddleware returns 402)
  - `customer.subscription.deleted` → set `IsActive = false`, send offboarding email
  - `customer.subscription.updated` → update `PlanTier` on Organization
- [ ] 14-day free trial: set on Stripe subscription at creation, no card required

**Angular tasks:**
- [ ] `/billing` route (OrgAdmin only) — current plan, usage stats, upgrade button, billing portal link
- [ ] Plan upgrade modal — show feature comparison table
- [ ] 402 interceptor in `jwt.interceptor.ts` — redirect to `/billing` with "Your subscription is inactive" message

**Verify:**
- New org gets 14-day trial, full Professional access
- Stripe `invoice.payment_failed` → next API request returns 402
- OrgAdmin opens billing portal → Stripe-hosted portal loads
- Plan upgrade → `PlanTier` updated, new features immediately available

---

### Feature 13 — Polish & Production Hardening
**Goal:** RLS enforced, full integration test suite passes, mobile UX complete, ready for first real client.

**Backend tasks:**
- [ ] Apply all RLS scripts from `db/rls/` — add org-level RLS for OrgAdmin queries
- [ ] EF Core interceptor sets `app.current_facility_id` + `app.current_organization_id` PostgreSQL session vars from JWT
- [ ] Integration tests in `KinCare.Tests`:
  - Coordinator cannot read another facility's data (same org)
  - OrgAdmin cannot read another org's data
  - Inactive org → 402 on all endpoints
  - Starter plan org → 402 on Professional features
  - State machine invalid transitions → 400
  - Twilio signature invalid → 403
  - Twilio MessageSid duplicate → 200, no second DB write
  - Uber Health webhook invalid → 403
  - Stripe webhook invalid → 400
- [ ] Global exception handler — RFC 7807 problem details, no stack traces
- [ ] Rate limiting: login 5/min/IP, Twilio webhook 60/min/IP, onboarding register 3/min/IP

**Angular tasks:**
- [ ] Mobile UX audit at 390px — every action in ≤ 3 taps
- [ ] Skeleton loading screens on dashboard and history
- [ ] Error toast for failed API calls, 402 redirect to billing
- [ ] Empty states for all lists
- [ ] Confirm dialog for ride cancel and coordinator deactivation

**Verify:**
- Full integration test suite — all pass
- End-to-end on real iPhone: register org → invite coordinator → book SMS ride → vendor replies → push notification received
- End-to-end on real iPhone: book Uber Health ride → Uber dispatches → status syncs to dashboard
- Stripe trial expiry → 402 → billing page → upgrade → access restored

---

## Key Domain Rules (Always Enforce)

```
Tenant hierarchy:
  Organization → Facility → AppUser/Resident/Vendor/Ride
  JWT must always contain: organization_id + role
  Coordinator JWT also contains: facility_id
  OrgAdmin JWT has no facility_id — can query all facilities in their org
  SuperAdmin has no org/facility scope — internal use only

Dispatch channel routing (DispatchRouter — never bypass):
  resident.NeedsWheelchair OR NeedsOxygen OR NeedsStretcher → SmsNemt (always)
  else org.PlanTier >= Professional AND facility.UberHealthEnabled → UberHealth
  else vendor.DispatchMethod == SmsTaxi → SmsTaxi
  else → SmsNemt (default fallback)
  No local vendor + org.BrokerEnabled → Broker

Valid state transitions (same for all channels):
  Dispatched  → Confirmed  (vendor_sms | uber_webhook | broker_webhook)
  Confirmed   → EnRoute    (vendor_sms | tracking_page | uber_webhook | broker_webhook)
  EnRoute     → Arrived    (vendor_sms | tracking_page | uber_webhook | broker_webhook)
  Arrived     → Dropped    (vendor_sms | tracking_page | uber_webhook | broker_webhook)
  Dropped     → Completed  (coordinator only)
  Any         → Cancelled  (coordinator only)

SMS reply map (SmsNemt and SmsTaxi only):
  1 → Accept         (Dispatched → Confirmed)
  2 → Decline        (Dispatched → Cancelled)
  3 → On My Way      (Confirmed  → EnRoute)
  4 → Arrived        (EnRoute    → Arrived)
  5 → Dropped Safely (Arrived    → Dropped)
  6 → Issue reported (no status change — FCM alert to coordinator)

Escalation rules (SmsNemt and SmsTaxi ONLY — never Uber Health or Broker):
  Dispatched + 30min past pickup_time   → "No confirmation" alert
  Confirmed  + 15min before pickup_time → "Hasn't departed" alert
  EnRoute    + 45min past pickup_time   → "May be delayed" alert
  Arrived    + 20min                    → "May need boarding help" alert

Plan feature gates:
  UberHealthDispatch  → Professional or Enterprise
  BrokerDispatch      → Professional or Enterprise
  SmartVendorTracking → Professional or Enterprise
  CsvExport           → Professional or Enterprise
  OrgDashboard        → Professional or Enterprise (OrgAdmin role required)
  SmsDispatch         → All plans (Starter included)
  EscalationAlerts    → All plans
```

## External Service Cost Notes

| Service | Cost | Notes |
|---|---|---|
| Twilio SMS | ~$0.008/msg outbound, ~$0.0075 inbound | ~$0.05/ride total. Bundle into subscription price. |
| Twilio phone number | $1.15/month | One shared number across all facilities — vendor lookup by phone number scoped to org |
| Uber Health | Per-ride rate (standard Uber pricing + platform fee) | Only ambulatory rides on Professional plan |
| Roundtrip Health | Per-ride broker fee | Fallback only — not primary dispatch |
| Firebase FCM | Free up to 500k messages/month | Essentially free at this scale |
| Stripe | 2.9% + $0.30 per transaction | Standard card processing. Use ACH for enterprise clients (0.8%, capped $5) |
| SendGrid (invitations) | Free up to 100 emails/day | Sufficient for onboarding emails |
| Vercel (Angular) | Free tier for MVP | Upgrade to Pro ($20/mo) when custom domain needed |
| Railway/Render (API) | ~$5-7/month | Never use free tier — cold starts break <5s SMS flow |
| Neon (PostgreSQL) | Free 0.5GB for MVP | Sufficient for first 6 months |

## Reference

- Full solution architecture: `solution-architectures/kincare-solution-architecture.md`
- Ideation decisions: `solution-ideations/kincare-ideation.md`
- Config template: `src/KinCare.API/appsettings.Development.json.example`
- Uber Health API docs: https://developer.uber.com/docs/riders/ride-requests/tutorials/api/introduction
- Roundtrip Health API: https://www.roundtriphealth.com/api
- Stripe subscriptions: https://stripe.com/docs/billing/subscriptions/overview
- Twilio IP ranges: https://help.twilio.com/articles/1260803965730
