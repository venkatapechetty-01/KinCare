# KinCare — Third-Party Services Inventory

**Version:** 1.0  
**Date:** 2026-07-01  
**Purpose:** Authoritative reference for every external service, SDK, and API used by KinCare — where it is used, how it is configured, what credentials are needed, and what is outstanding.

---

## Table of Contents

1. [Communication Services](#1-communication-services)
2. [Maps & Location](#2-maps--location)
3. [Dispatch & Transport APIs](#3-dispatch--transport-apis)
4. [Billing & Payments](#4-billing--payments)
5. [Real-Time & WebSockets](#5-real-time--websockets)
6. [Push Notifications](#6-push-notifications)
7. [API Documentation (Swagger)](#7-api-documentation-swagger)
8. [Monitoring & Observability](#8-monitoring--observability)
9. [Background Jobs](#9-background-jobs)
10. [Authentication & Identity](#10-authentication--identity)
11. [Frontend Libraries](#11-frontend-libraries)
12. [Configuration Reference](#12-configuration-reference)
13. [TODO — Outstanding Setup](#13-todo--outstanding-setup)

---

## 1. Communication Services

### Twilio SMS

| Field | Value |
|---|---|
| **Purpose** | Outbound SMS dispatch to transport vendors; inbound webhook to receive numbered replies (1–9) |
| **SDK** | `Twilio` NuGet package v7.x |
| **Config key** | `Twilio:AccountSid`, `Twilio:AuthToken`, `Twilio:FromNumber` |
| **Config file** | `src/KinCare.API/appsettings.Development.json` (git-ignored) |
| **Inbound webhook** | `POST /webhook/twilio` |
| **Webhook validation** | `X-Twilio-Signature` HMAC-SHA1 via `RequestValidator.Validate()` — returns 403 on failure |
| **Dev bypass** | Signature validation skipped when `Twilio:AuthToken` is empty string (test environment only — never deploy empty) |
| **Idempotency** | `MessageSid` stored in `ride_events.notes` — duplicate webhooks rejected |
| **Rate limit** | 60 requests/min/IP on `/webhook/twilio` |
| **IP allowlist** | Twilio published IP ranges — configure at reverse proxy or middleware in production |
| **Cost** | ~$0.008/msg outbound, ~$0.0075 inbound, ~$0.05/ride total; $1.15/month for one shared number |
| **Local testing** | Requires ngrok tunnel — `ngrok http 5000` → paste URL into Twilio webhook console |
| **Key files** | `Services/Dispatch/TwilioDispatchService.cs`, `Webhooks/TwilioWebhookHandler.cs`, `Infrastructure/TwilioConfig.cs` |
| **Status** | ✅ Fully implemented |

---

### SendGrid (Invitation Emails)

| Field | Value |
|---|---|
| **Purpose** | Transactional email for coordinator invitation links; org registration confirmation |
| **SDK** | Referenced in architecture; direct HTTP or SendGrid .NET SDK |
| **Config key** | `SendGrid:ApiKey` (not yet in appsettings template) |
| **Free tier** | Up to 100 emails/day — sufficient for onboarding at MVP scale |
| **Key files** | `Endpoints/OnboardingEndpoints.cs` (invite email send point) |
| **Status** | ⚠️ Email sending referenced but `SendGrid:ApiKey` not yet wired into `appsettings.Development.json.example` |
| **TODO** | Add `SendGrid:ApiKey` to `appsettings.Development.json.example` and wire `SendGridClient` into the invite flow |

---

## 2. Maps & Location

### Google Maps JavaScript API

| Field | Value |
|---|---|
| **Purpose** | Live GPS map embed on ride detail screen; navigation deeplinks on driver tracking page; Google Maps URL in booking confirmation |
| **SDK** | Client-side JS (`@googlemaps/js-api-loader` or direct script tag on tracking page) |
| **Config key** | `googleMapsApiKey` in `src/KinCare.Web/src/environments/environment.development.ts` |
| **Current value** | `''` (empty string — map will not render) |
| **Cost** | Free tier: $200/month credit (~28,000 map loads/month) — sufficient at MVP scale |
| **Where used** | `live-map/live-map.component.ts` (dashboard GPS overlay), `ride-detail` (driver pin), `TrackingEndpoints.cs` (deeplink URL construction) |
| **Status** | ⚠️ SDK wired but API key not configured — map panels will not render |
| **TODO** | Obtain key from Google Cloud Console → APIs & Services → Credentials; fill in `environment.development.ts` and production environment variable |

---

### Browser Geolocation API

| Field | Value |
|---|---|
| **Purpose** | GPS coordinate capture on driver tracking page — `navigator.geolocation.watchPosition()` |
| **SDK** | Native browser API — no external dependency |
| **Where used** | `TrackingEndpoints.cs` (tracking page JavaScript inline) |
| **Cadence** | POST lat/lng to `POST /api/rides/location` every 30 seconds when permission granted |
| **Fallback** | If geolocation permission denied, tracking page still works for one-tap status buttons via SMS flow |
| **Status** | ✅ Implemented |

---

## 3. Dispatch & Transport APIs

### Uber Health API

| Field | Value |
|---|---|
| **Purpose** | Ambulatory ride dispatch for Professional+ plan organizations — no SMS, Uber-managed routing |
| **Auth** | OAuth2 client credentials flow |
| **Config keys** | `UberHealth:ClientId`, `UberHealth:ClientSecret` |
| **Config file** | `src/KinCare.API/appsettings.Development.json` (git-ignored) |
| **Inbound webhook** | `POST /webhook/uber-health` |
| **Webhook validation** | Signature header validation — returns 403 on failure |
| **Plan gate** | `PlanFeature.UberHealthDispatch` — requires Professional or Enterprise plan |
| **External trip ID** | Stored in `rides.external_trip_id` for webhook lookup |
| **Fallback sync** | `ExternalTripSyncJob` — every 2 min (HTTP polling not yet implemented — currently logs only) |
| **Docs** | https://developer.uber.com/docs/riders/ride-requests/tutorials/api/introduction |
| **Key files** | `Services/Dispatch/UberHealthDispatchService.cs`, `Webhooks/UberHealthWebhookHandler.cs`, `Infrastructure/UberHealthConfig.cs` |
| **Status** | ✅ Wired — webhooks functional; **TODO: `ExternalTripSyncJob` HTTP polling calls not implemented** |

---

### Uber Business API

| Field | Value |
|---|---|
| **Purpose** | Supplementary Uber dispatch (non-Health endpoint) |
| **Config keys** | `UberBusiness:ClientId`, `UberBusiness:ClientSecret` |
| **Status** | ⚠️ Config keys defined; implementation details TBD |

---

### Roundtrip Health Broker API

| Field | Value |
|---|---|
| **Purpose** | NEMT broker fallback — used when no local vendor is available and org has `BrokerEnabled` |
| **Auth** | API key |
| **Config key** | `Broker:ApiKey` |
| **Config file** | `src/KinCare.API/appsettings.Development.json` (git-ignored) |
| **Inbound webhook** | `POST /webhook/broker` |
| **Plan gate** | `PlanFeature.BrokerDispatch` — requires Professional or Enterprise plan |
| **Fallback sync** | `ExternalTripSyncJob` — every 2 min (HTTP polling not yet implemented) |
| **Docs** | https://www.roundtriphealth.com/api |
| **Key files** | `Services/Dispatch/BrokerDispatchService.cs`, `Webhooks/BrokerWebhookHandler.cs`, `Infrastructure/BrokerConfig.cs` |
| **Status** | ✅ Wired — webhooks functional; **TODO: `ExternalTripSyncJob` HTTP polling calls not implemented** |

---

## 4. Billing & Payments

### Stripe

| Field | Value |
|---|---|
| **Purpose** | SaaS subscription billing — plan management, payment collection, plan tier enforcement |
| **SDK** | `Stripe.net` NuGet package |
| **Config keys** | `Stripe:ApiKey` (secret key), `Stripe:WebhookSecret` |
| **Config file** | `src/KinCare.API/appsettings.Development.json` (git-ignored) |
| **Inbound webhook** | `POST /webhook/stripe` |
| **Webhook validation** | `EventUtility.ConstructEvent` with `Stripe-Signature` header |
| **Webhook events handled** | `invoice.paid` (activate org), `invoice.payment_failed` (deactivate org), `customer.subscription.deleted` (deactivate + offboarding email), `customer.subscription.updated` (update PlanTier) |
| **Free trial** | 14-day, no card required — set on Stripe subscription at creation |
| **Org deactivation** | Sets `Organization.IsActive = false` → `TenantMiddleware` returns 402 → Angular redirects to `/billing` |
| **Portal** | Stripe Customer Portal — `GET /api/billing/portal` returns self-service URL |
| **Cost** | 2.9% + $0.30 per card transaction; 0.8% capped $5 for ACH (use for enterprise clients) |
| **Local testing** | Requires Stripe CLI or ngrok for webhook delivery — `stripe listen --forward-to localhost:5000/webhook/stripe` |
| **Key files** | `Endpoints/BillingEndpoints.cs`, `Webhooks/StripeWebhookHandler.cs`, `Infrastructure/StripeConfig.cs` |
| **Status** | ✅ Fully implemented — **TODO: test end-to-end with real Stripe test-mode key on publicly accessible URL** |

---

## 5. Real-Time & WebSockets

### ASP.NET Core SignalR

| Field | Value |
|---|---|
| **Purpose** | Real-time ride status and GPS location updates pushed from server to Angular dashboard — eliminates polling |
| **Server SDK** | `Microsoft.AspNetCore.SignalR` (built into ASP.NET Core 9 — no extra package) |
| **Client SDK** | `@microsoft/signalr` ^10.0.0 (NPM) |
| **Hub endpoint** | `/hubs/ride-status` |
| **Auth** | JWT passed via query string `?access_token=...` (standard SignalR pattern for WebSockets — not a security gap) |
| **Groups** | Coordinators join `facility:{facility_id}` group on connect — broadcast is facility-scoped |
| **Events** | `RideStatusChanged(rideId, newStatus)` — broadcast on every `AdvanceStatusAsync`; `LocationUpdated(rideId, lat, lng)` — broadcast on GPS update |
| **Reconnection** | `withAutomaticReconnect()` on client — silently reconnects if WebSocket drops |
| **Angular** | `HubConnection` initialized in `DashboardComponent.ngOnInit()`, stopped in `ngOnDestroy()` |
| **Key files** | `Hubs/RideStatusHub.cs`, `dashboard/dashboard.component.ts` |
| **Status** | ✅ Fully implemented (server 2026-06-29, client wired 2026-07-01) |

---

## 6. Push Notifications

### Firebase Cloud Messaging (FCM)

| Field | Value |
|---|---|
| **Purpose** | Real push notifications to coordinator's phone — arrival alerts, safe dropoff, escalations, driver issue reports |
| **Server SDK** | `FirebaseAdmin` NuGet package (Firebase Admin .NET SDK) |
| **Client SDK** | Firebase JS SDK v10.x + Angular Service Worker (`@angular/pwa`) |
| **Auth** | Firebase service account JSON file — path configured in appsettings |
| **Config key** | `Firebase:CredentialPath` — path to `firebase-service-account.json` |
| **Credential file** | `firebase-service-account.json` — **never commit to git** (in `.gitignore`) |
| **Push triggers** | EnRoute (driver on way), Arrived (driver outside), PickedUp (resident picked up), Dropped (safe delivery), Escalation (4 thresholds), Reply 9 (driver issue) |
| **Device registration** | `POST /api/devices/register` — coordinator POSTs FCM token on first login |
| **FCM token storage** | `AppUser.FcmToken` column — refreshed on each login |
| **Cost** | Free up to 500k messages/month — effectively free at KinCare scale |
| **Local testing** | Requires `firebase-service-account.json` at configured path; push to real device requires HTTPS (ngrok in dev) |
| **Key files** | `Services/FcmService.cs`, `Endpoints/DeviceEndpoints.cs`, `Infrastructure/FcmConfig.cs` |
| **Angular files** | `app.config.ts` (`provideServiceWorker`), login component (FCM token registration) |
| **Status** | ✅ Implemented — **TODO: test on real iOS/Android device; `firebase-service-account.json` path must be configured in appsettings.Development.json** |

---

## 7. API Documentation (Swagger / OpenAPI)

### Swagger UI (Swashbuckle)

| Field | Value |
|---|---|
| **Purpose** | Interactive API documentation and manual endpoint testing in development |
| **SDK** | `Swashbuckle.AspNetCore` NuGet package |
| **Dev endpoint** | `http://localhost:5000/swagger` |
| **Production** | Disabled in production (`if (app.Environment.IsDevelopment())` guard) |
| **Auth support** | JWT bearer auth configured in Swagger UI — paste token into Authorize dialog to test protected endpoints |
| **OpenAPI spec** | `GET /swagger/v1/swagger.json` — machine-readable spec usable for client generation |
| **Key files** | `Program.cs` — `builder.Services.AddEndpointsApiExplorer()`, `builder.Services.AddSwaggerGen(...)`, `app.UseSwagger()`, `app.UseSwaggerUI()` |
| **Status** | ✅ Available in development |
| **TODO** | Ensure Swagger is confirmed disabled in production build; add XML doc comments to key endpoints for richer Swagger descriptions |

---

## 8. Monitoring & Observability

### Serilog (Structured Logging)

| Field | Value |
|---|---|
| **Purpose** | Structured application logging — request tracing, error capture, security event logging |
| **SDK** | `Serilog.AspNetCore` NuGet package |
| **Sinks** | File sink (local dev), Console sink (production) |
| **Log levels** | Development: `Debug` for auth (⚠️ revert to `Warning` before staging); Production: `Warning` minimum |
| **Security events logged** | Failed JWT validation (with IP), Twilio signature failures, rate limit violations, cross-tenant access attempts |
| **PII policy** | Resident special needs (wheelchair, oxygen) **not** logged in application logs |
| **Key files** | `Program.cs` (Serilog bootstrap), `appsettings.json` (log level config) |
| **Status** | ✅ Implemented — **TODO: revert auth debug logging to Warning before staging** |

---

### Splunk (Planned)

| Field | Value |
|---|---|
| **Purpose** | Centralized log aggregation, security alerting, operational dashboards — recommended for production |
| **Integration** | Serilog → Splunk sink (`Serilog.Sinks.Splunk`) or Splunk HTTP Event Collector (HEC) |
| **Config** | Splunk HEC URL + token in environment variables |
| **Status** | ⏸ **Not yet implemented** — Serilog file/console sink in use; Splunk integration planned for production hardening |
| **TODO** | Add `Serilog.Sinks.Splunk` NuGet package; configure Splunk HEC endpoint and token; set up security alert rules for 403/429 spikes and cross-tenant access attempts |

---

### Health Checks

| Field | Value |
|---|---|
| **Purpose** | Liveness and readiness probe — used by hosting platform (Railway/Render) and uptime monitors |
| **Endpoint** | `GET /health` |
| **Checks** | PostgreSQL database connectivity |
| **Response** | `Healthy` / `Unhealthy` + DB check detail |
| **SDK** | `Microsoft.Extensions.Diagnostics.HealthChecks` (built in) + `AspNetCore.HealthChecks.Npgsql` |
| **Status** | ✅ Implemented |

---

## 9. Background Jobs

### Hangfire

| Field | Value |
|---|---|
| **Purpose** | Time-based escalation alerts; per-ride checkpoint reminders; external trip status polling |
| **SDK** | `Hangfire` + `Hangfire.PostgreSql` NuGet packages |
| **Storage** | Same PostgreSQL instance (`hangfire` schema) — no Redis required |
| **Dashboard** | `/hangfire` — `LocalRequestsOnlyAuthorizationFilter` in dev; role-based auth in production |
| **Jobs** | `EscalationJob` (every 5 min), `CheckpointReminderJob` (per-ride), `ExternalTripSyncJob` (every 2 min — HTTP polling not yet implemented) |
| **Escalation scope** | SMS channels (`SmsNemt`, `SmsTaxi`) only — never fires for Uber Health or Broker rides |
| **Idempotency** | Checks `ride_events` for prior escalation of same type before firing |
| **Status** | ✅ Escalation + Checkpoint implemented; **TODO: implement HTTP polling in `ExternalTripSyncJob`** |

---

## 10. Authentication & Identity

### ASP.NET Core Identity

| Field | Value |
|---|---|
| **Purpose** | User account management — password hashing, account creation, email confirmation, coordinator deactivation |
| **SDK** | `Microsoft.AspNetCore.Identity.EntityFrameworkCore` (built into .NET 9) |
| **Password hashing** | PBKDF2 with HMAC-SHA256 (ASP.NET Core Identity default) |
| **JWT issuance** | Custom `TokenService` — access tokens (15 min), refresh tokens (7 days, stored in `RefreshTokens` table) |
| **Token claims** | `organization_id`, `facility_id`, `role` always present |
| **Refresh rotation** | Full-family revocation on token reuse detection |
| **Deactivated users** | `TokenService` checks `User.IsActive` on refresh — immediately revokes and returns 401 |
| **Invite tokens** | `RandomNumberGenerator.GetBytes(32)` → Base64 (256-bit CSPRNG) |
| **Status** | ✅ Fully implemented |

### AspNetCoreRateLimit

| Field | Value |
|---|---|
| **Purpose** | IP-based rate limiting to prevent brute force, replay attacks, and abuse |
| **SDK** | `AspNetCoreRateLimit` NuGet package |
| **Config** | `IpRateLimit` section in `appsettings.json` |
| **Rules** | Login: 5/min; Register: 3/min; Twilio webhook: 60/min; Location: 12/min; General: 30/sec |
| **Test bypass** | `IpRateLimit:EnableEndpointRateLimiting=false` in `CustomWebApplicationFactory.cs` |
| **Status** | ✅ Implemented |

---

## 11. Frontend Libraries

### Angular Material

| Field | Value |
|---|---|
| **Purpose** | Primary UI component library — mobile-first, 390px primary breakpoint |
| **Version** | 17.x |
| **Key components** | `MatCard`, `MatChips`, `MatDialog`, `MatSnackBar`, `MatPaginator`, `MatTable`, `MatFab`, `MatBottomSheet`, `MatSelect`, `MatCheckbox` |
| **Status** | ✅ In active use across all screens |

---

### GSAP (GreenSock Animation Platform)

| Field | Value |
|---|---|
| **Purpose** | Card entry animations on dashboard; status badge scale animations |
| **Version** | 3.x |
| **Usage** | `gsap.from('.ride-card', { duration:0.4, y:24, opacity:0, stagger:0.08, ease:'power2.out', clearProps:'all' })` |
| **Key files** | `dashboard/dashboard.component.ts` |
| **Status** | ✅ Implemented |

---

### ngx-lottie

| Field | Value |
|---|---|
| **Purpose** | Lottie animation player for complex vector animations (loading states, empty states) |
| **Version** | 9.1.0 (not `@lottiefiles/ngx-lottie` — unavailable) |
| **Lazy loading** | `provideLottieOptions({ player: () => import('lottie-web') })` in `app.config.ts` |
| **Status** | ✅ Configured — animations added as needed |

---

### Zod

| Field | Value |
|---|---|
| **Purpose** | Runtime API response validation — catches schema drift between backend and frontend before silent bugs |
| **Version** | 4.4.3 |
| **Location** | `src/KinCare.Web/src/app/shared/schemas/api.schemas.ts` |
| **Schemas** | `RideStatusSchema` (all 9 statuses), `RideSchema`, `RideDetailSchema`, `ResidentSchema`, `VendorSchema` |
| **Status** | ✅ Implemented |

---

### @microsoft/signalr (client)

See [Section 5 — Real-Time & WebSockets](#5-real-time--websockets).

---

### Firebase JS SDK + Angular PWA

See [Section 6 — Push Notifications](#6-push-notifications).

---

## 12. Configuration Reference

### `appsettings.Development.json` (git-ignored — never commit)

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=kincare;Username=postgres;Password=..."
  },
  "Jwt": {
    "SecretKey": "<min 32 char random string>",
    "Issuer": "kincare-api",
    "Audience": "kincare-app",
    "AccessTokenExpiryMinutes": 15,
    "RefreshTokenExpiryDays": 7
  },
  "Twilio": {
    "AccountSid": "AC...",
    "AuthToken": "...",
    "FromNumber": "+1..."
  },
  "Stripe": {
    "ApiKey": "sk_test_...",
    "WebhookSecret": "whsec_..."
  },
  "UberHealth": {
    "ClientId": "...",
    "ClientSecret": "..."
  },
  "UberBusiness": {
    "ClientId": "...",
    "ClientSecret": "..."
  },
  "Broker": {
    "ApiKey": "..."
  },
  "Firebase": {
    "CredentialPath": "./firebase-service-account.json"
  },
  "SendGrid": {
    "ApiKey": "SG...."
  }
}
```

> **Note:** `firebase-service-account.json` is a separate file that must be placed at the path specified in `Firebase:CredentialPath`. It is git-ignored and must never be committed.

### `src/KinCare.Web/src/environments/environment.development.ts`

```typescript
export const environment = {
  production: false,
  apiUrl: 'http://localhost:5000',
  googleMapsApiKey: ''  // Fill in from Google Cloud Console → APIs & Services → Credentials
};
```

---

## 13. TODO — Outstanding Setup

| Priority | Service | Task |
|---|---|---|
| 🔴 High | Firebase FCM | Place `firebase-service-account.json` at configured path; test push notification on real iOS/Android device |
| 🔴 High | Google Maps | Fill in `googleMapsApiKey` in `environment.development.ts` — live map and tracking page deeplinks will not work without it |
| 🔴 High | Hangfire `ExternalTripSyncJob` | Implement actual HTTP polling calls to Uber Health and Roundtrip Health APIs — currently logs only. Uber/Broker ride status will stall if webhook is missed. |
| 🟡 Medium | Stripe | Test full billing pipeline end-to-end with real Stripe test-mode key on a publicly accessible URL (Stripe requires HTTPS to deliver webhooks) |
| 🟡 Medium | SendGrid | Wire `SendGrid:ApiKey` into invitation email send — currently referenced in architecture but not in `appsettings.Development.json.example` |
| 🟡 Medium | Splunk | Add `Serilog.Sinks.Splunk` for production log aggregation and security alerting; configure HEC endpoint |
| 🟡 Medium | Swagger | Add XML doc comments to key endpoint groups for richer API documentation |
| 🟡 Medium | E2E (Playwright) | Run the 7 Playwright specs in `e2e/` — require `E2E_TEST_EMAIL` + `E2E_TEST_PASSWORD` env vars and a running server; never yet executed |
| 🟢 Low | Serilog | Revert `Microsoft.AspNetCore.Authentication` and `Microsoft.IdentityModel` log levels from `Debug` to `Warning` before any staging deployment |
| 🟢 Low | CI/CD | Set up GitHub Actions: build → test → deploy on merge to main; separate staging and production environments |
| 🟢 Low | ngrok | Document ngrok setup for new developers; ensure Twilio, Stripe, Uber Health, and Broker webhook URLs are all updated when ngrok restarts (new URL each session unless paid plan) |

---
Generated by Rocket Flow · 2.0.16 · 2026-07-01
