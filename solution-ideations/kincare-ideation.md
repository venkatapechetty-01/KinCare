# KinCare Architecture Ideation

**Version:** 2.0  
**Date:** 2026-07-01  
**Status:** Finalised — all decisions implemented

---

## Decision 1: API Hosting
- **Decision:** Local development for now; production deployment deferred
- **Frontend:** Vercel (Angular static build)
- **Backend:** TBD — Railway, Render, or Azure App Service (Docker container)
- **Database:** Local PostgreSQL for dev; Neon / Railway Postgres / Supabase Postgres for production
- **TODO:** Set up staging environment, GitHub Actions CI/CD pipeline, production deployment

---

## Decision 2: Database
- **Decision:** Local PostgreSQL 16 + ASP.NET Core Identity + EF Core 9 + Npgsql
- **Rationale:** No vendor lock-in; full schema control; EF Core migrations manage schema; JWT auth native inside .NET 9 API; connection string swappable to any hosted Postgres at deploy time.
- **Replaces:** Supabase Auth + Supabase-hosted Postgres (eliminated — vendor lock-in)
- **Multi-tenancy:** EF Core global query filters (facility_id / organization_id) + PostgreSQL RLS policies — two independent enforcement layers
- **Status:** ✅ Implemented — all migrations, indexes, and RLS scripts in place

---

## Decision 3: Local Webhook Development
- **Decision:** ngrok
- **Rationale:** Single command tunnels local .NET API to public HTTPS URL for Twilio, Stripe, Uber Health, and Broker webhook testing
- **Status:** ✅ In active use

---

## Decision 4: Push Notifications
- **Decision:** Firebase Cloud Messaging (FCM) with Angular PWA service worker
- **Rationale:** Full production B2B product — real push notifications to coordinator's phone. Angular 17 PWA with service worker, FCM token registration, real-time arrival alerts.
- **Implementation:** `ng add @angular/pwa`, Firebase JS SDK in Angular, Firebase Admin .NET SDK in API (`FcmService`)
- **Push triggers:** Arrived (driver outside), Dropped (safe delivery), Escalation (silent driver), Reply 9 (driver issue report)
- **Status:** ✅ Implemented — **TODO: test on real iOS/Android device; `firebase-service-account.json` path must be configured**

---

## Decision 5: Message Infrastructure
- **Decision:** No Kafka / SQS / SNS
- **Rationale:** KinCare is a focused B2B SaaS at senior facility scale. Messaging infrastructure is overkill. Direct HTTP calls from .NET API to Twilio, FCM, Uber Health, and Broker are sufficient. Internal event log handled by the `ride_events` table in Postgres.
- **Status:** ✅ Confirmed — no queue needed at current scale

---

## Decision 6: Ride Status State Machine
- **Decision:** Strict 9-state machine + automated time-based escalation alerts
- **States (final):**
  ```
  Dispatched → Confirmed → EnRoute → Arrived → PickedUp → AtDestination → Dropped → Completed
  Any state  → Cancelled (coordinator only)
  ```
- **Original 6 states expanded to 9** — added `PickedUp` and `AtDestination` to capture full custody chain required for senior living transport compliance (resident picked up ≠ at destination ≠ dropped off)
- **Auto-transitions:** Twilio webhook reply parsing, Uber Health webhooks, Broker webhooks, tracking page buttons
- **Escalation:** Hangfire `EscalationJob` checks time thresholds and fires FCM alerts when SMS driver goes silent. **Never fires for Uber Health or Broker rides** — those platforms manage their own escalation.
- **Status:** ✅ Fully implemented and tested (233 unit + 23 integration tests passing)

---

## Decision 7: Driver Communication — Adaptive SMS
- **Decision:** Single-digit numeric replies (1–9) — works on any phone
- **Rationale:** Basic flip phones, no autocorrect issues, zero driver training needed
- **Reply map (final — 9 replies):**

  | Reply | Meaning | Transition |
  |---|---|---|
  | 1 | Accept | Dispatched → Confirmed |
  | 2 | Decline | Dispatched → Cancelled |
  | 3 | On My Way | Confirmed → EnRoute |
  | 4 | Arrived at Pickup | EnRoute → Arrived |
  | 5 | Resident Picked Up | Arrived → PickedUp |
  | 6 | At Destination | PickedUp → AtDestination |
  | 7 | Dropped Safely | AtDestination → Dropped |
  | 8 | Completed | Dropped → Completed |
  | 9 | Issue / Need Help | No status change — FCM alert to coordinator |

- **Status:** ✅ Fully implemented — `TwilioWebhookHandler.PostAcceptReplyMap`

---

## Decision 8: Smart Vendor Progressive Enhancement
- **Decision:** `capability_tier` flag on vendor record (Basic | Smart)
- **Basic vendors:** Numbered SMS replies only — full audit chain, works on any phone
- **Smart vendors:** Same SMS + tokenized tracking URL in booking SMS
- **Tracking page:** Lightweight HTML served by .NET API (non-Angular), one-tap status buttons, Google Maps deeplinks, optional GPS sharing every 30 seconds
- **GPS storage:** Last-known `lat/lng` + `last_location_at` on ride record (not full route history for MVP)
- **Coordinator dashboard:** Live 📍 indicator if `last_location_at` < 10 min ago; full GPS map embed on ride detail; Open Map link to Google Maps
- **Token security:** UUID v4 (122 bits entropy), nulled immediately on `Completed` or `Cancelled`; invalid → 404; expired (terminal state) → 410 Gone
- **Status:** ✅ Implemented — **TODO: Google Maps API key required in `environment.development.ts`**

---

## Decision 9: Background Job Scheduler
- **Decision:** Hangfire embedded in .NET 9 API
- **Storage:** Same PostgreSQL instance (separate `hangfire` schema)
- **Jobs:**
  - `EscalationJob` — recurring every 5 min; 4 thresholds (no confirmation, hasn't departed, may be delayed, may need boarding help); SMS channels only
  - `CheckpointReminderJob` — per-ride scheduled jobs on status advance
  - `ExternalTripSyncJob` — every 2 min; fallback polling for Uber Health / Broker rides when webhooks are missed — **currently logs only, HTTP calls not yet implemented**
- **Dashboard:** `/hangfire` — localhost-only in dev; role-based auth in production (never public)
- **Status:** ✅ EscalationJob + CheckpointReminderJob fully implemented — **TODO: implement HTTP polling in ExternalTripSyncJob**

---

## Decision 10: Real-Time Dashboard — SignalR
- **Decision:** ASP.NET Core SignalR (`RideStatusHub`) — no polling
- **Rationale:** CLAUDE.md requirement: "use SignalR for ride status updates to Angular — no polling." Eliminates any stale data scenario.
- **Hub:** `RideStatusHub` — coordinators join `facility:{facility_id}` group on connect; JWT passed via query string (standard SignalR pattern)
- **Events broadcast:**
  - `RideStatusChanged(rideId, newStatus)` — every `RideService.AdvanceStatusAsync` call
  - `LocationUpdated(rideId, lat, lng)` — every GPS coordinate POST from tracking page
- **Angular:** `HubConnection` with `withAutomaticReconnect()` in `DashboardComponent`; disconnects on `ngOnDestroy`
- **Status:** ✅ Fully implemented (2026-07-01)

---

## Decision 11: Multi-Tenant B2B SaaS Architecture
- **Decision:** Organization → Facility → User/Resident/Vendor/Ride hierarchy
- **Roles:** `SuperAdmin` (KinCare staff), `OrgAdmin` (client admin), `Coordinator` (day-to-day user)
- **Plan tiers:** `Starter` (SMS dispatch, escalation, history), `Professional` (+ Uber Health, GPS tracking, CSV export), `Enterprise` (+ multi-facility dashboard, API access, SSO)
- **Plan enforcement:** `IPlanGate.Requires(org, PlanFeature)` in API middleware only — Angular never gates features
- **JWT claims:** Always include `organization_id` + `role`; Coordinator JWT also includes `facility_id`
- **TenantMiddleware:** Runs on every authenticated request; validates `Organization.IsActive`; returns 402 if inactive
- **Status:** ✅ Fully implemented

---

## Decision 12: Stripe Subscription Billing
- **Decision:** Stripe .NET SDK with webhook-driven plan enforcement
- **Flow:** Org registers → Stripe customer created → subscribe to plan → 14-day free trial (no card required) → webhooks drive `Organization.IsActive` and `PlanTier`
- **Webhooks handled:** `invoice.paid` (activate), `invoice.payment_failed` (deactivate), `customer.subscription.deleted` (deactivate + offboarding email), `customer.subscription.updated` (update PlanTier)
- **402 interceptor:** Angular `jwt.interceptor.ts` redirects to `/billing` with "Your subscription is inactive" on 402 responses
- **Status:** ✅ Implemented — **TODO: test end-to-end with real Stripe test-mode key on publicly accessible URL**

---

## Decision 13: Dispatch Channel Strategy
- **Decision:** `DispatchRouter` is the only entry point for all dispatch — never call channel services directly
- **Channels:**

  | Channel | When | How |
  |---|---|---|
  | `SmsNemt` | Wheelchair / oxygen / stretcher resident, or fallback | Twilio SMS, numbered replies 1–9 |
  | `SmsTaxi` | Ambulatory, taxi vendor, no Uber Health | Twilio SMS, identical flow to SmsNemt (no special needs tags) |
  | `UberHealth` | Ambulatory, Professional+ plan, facility.UberHealthEnabled | Uber Health API, webhook status updates |
  | `Broker` | No local vendor, org.BrokerEnabled | Roundtrip Health API, webhook status updates |

- **Broadcast dispatch model:** All capable vendors notified simultaneously via `RideDispatchOffer` table. First to reply "1" claims via DB transaction (`ClaimRideAsync`). Prevents single-vendor no-show with no fallback.
- **Status:** ✅ Fully implemented — SmsTaxi and SmsNemt use identical `TwilioDispatchService` (only difference: special needs tags omitted for SmsTaxi)

---

## Decision 14: Animation & UX Libraries
- **Decision:** GSAP + ngx-lottie (lazy-loaded) + Angular Material skeleton loading
- **GSAP:** Card entry animations (`gsap.from('.ride-card', { y:24, opacity:0, stagger:0.08 })`); status badge scale pulse
- **ngx-lottie v9.1.0:** Lazy-loaded Lottie player (`provideLottieOptions({ player: () => import('lottie-web') })`)
- **Skeleton loading:** CSS shimmer animation on dashboard while rides load (no `<mat-spinner>` blocking the grid)
- **Paginator:** `MatPaginator` on residents list at 12/page (page size options: 12, 24, 48)
- **Status:** ✅ Implemented

---

## Decision 15: Runtime API Validation (Zod)
- **Decision:** Zod v4.4.3 for runtime API response type validation in Angular
- **Location:** `src/KinCare.Web/src/app/shared/schemas/api.schemas.ts`
- **Schemas:** `RideStatusSchema` (all 9 statuses), `RideSchema`, `RideDetailSchema`, `ResidentSchema`, `VendorSchema`
- **Rationale:** TypeScript types are compile-time only. Zod validates the actual shape of API responses at runtime, catching schema drift between backend and frontend before it causes silent bugs.
- **Status:** ✅ Implemented

---

## Decision 16: Phone Number Deduplication (Vendors)
- **Decision:** 409 Conflict returned by API when vendor phone already exists in facility; Angular surfaces inline form error (not snackbar)
- **Flow:** `POST /api/vendors` returns 409 → Angular catches it → `phoneNumber` control gets `setErrors({ duplicate: true })` → inline `<mat-error>` shows "A driver with this phone number already exists"
- **Rationale:** Phone number is the vendor lookup key for Twilio inbound webhook. Duplicates would cause ambiguous ride claims.
- **Status:** ✅ Implemented

---
Generated by Rocket Flow · 2.0.16 · 2026-07-01
