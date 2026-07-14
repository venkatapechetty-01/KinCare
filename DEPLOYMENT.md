# Deployment Guide

## Render test environment

This repository is prepared for a simple Render deployment with three pieces:

- PostgreSQL database
- .NET API
- Angular frontend

### 1. Create the services in Render

1. Connect this GitHub repository to Render.
2. Create a new Blueprint using the included [render.yaml](render.yaml) file.
3. Render will create:
   - a PostgreSQL database
   - a backend web service
   - a frontend static site

### 2. Set required environment variables

.NET reads nested config keys like `Jwt:SecretKey` from environment variables using a
double-underscore separator (`Jwt__SecretKey`) — that's the naming convention `render.yaml`
uses for every API env var, and it must be followed exactly for a value to actually reach
the app's configuration.

The API will not start without:
- `ConnectionStrings__DefaultConnection` — populated automatically by the blueprint from the `kincare-db` database
- `Jwt__SecretKey` — generated automatically by the blueprint (`generateValue: true`)

The API will start without the rest, but the corresponding feature won't work until set:
- `App__BaseUrl` / `Cors__AllowedOrigins__0` — set to the frontend's actual Render URL (update these if `kincare-web.onrender.com` isn't available and Render assigns a different hostname)
- `Twilio__AccountSid`, `Twilio__AuthToken`, `Twilio__FromNumber` — SMS dispatch
- `Stripe__SecretKey`, `Stripe__WebhookSecret`, `Stripe__StarterPriceId`, `Stripe__ProfessionalPriceId`, `Stripe__EnterpriseId` — billing
- `SendGrid__ApiKey`, `SendGrid__FromEmail` — invitation emails
- `Broker__ApiKey`, `Broker__ClientId`, `Broker__ClientSecret`, `Broker__OrganizationId`, `Broker__WebhookSecret` — Roundtrip Health fallback dispatch
- `Splunk__HecUrl`, `Splunk__HecToken` — log shipping
- `LocationIq__ApiKey` — address autocomplete on the booking form (backend proxy)

These are all marked `sync: false` in `render.yaml`, so Render will prompt for each at
Blueprint creation time — leave any you don't have blank for now, the app boots fine without them.

### 3. Frontend build-time variables

The frontend build (`scripts/write-env.js`, run via `npm run build`) reads two env vars and
bakes them into `environment.production.ts` at build time — this is the **only** placeholder-
substitution mechanism that actually runs on the Render deploy path (a separate `sed`-based
substitution exists in `.github/workflows/deploy-*.yml` for the older Vercel/Railway pipeline,
but that workflow is not what `render.yaml` uses):

- `API_BASE_URL=https://<your-api-service>.onrender.com` — set automatically by `render.yaml`
- `LOCATIONIQ_API_KEY=<your LocationIQ key>` — powers booking-form autocomplete requests made
  directly from the browser to your API, and the `/live-map` Leaflet tile layer; marked
  `sync: false` in `render.yaml`, so Render prompts for it at Blueprint creation

If you deploy manually rather than with the blueprint, set both of the above as Render frontend
build environment variables.

Note: `GOOGLE_MAPS_API_KEY_PLACEHOLDER`, `FCM_VAPID_KEY_PLACEHOLDER`, and the `FIREBASE_*_PLACEHOLDER`
tokens in `environment.production.ts` are **not** substituted by this script or by `render.yaml` —
they'll ship as literal placeholder strings in the production bundle unless you extend
`write-env.js` the same way. This only affects the ride-detail page's individual Google Maps
Embed iframe and Firebase push notifications; it does not affect the `/live-map` operations map
(Leaflet + LocationIQ), which has no Google or Firebase dependency.

### 4. Database migration

The API now applies pending EF Core migrations automatically on startup. If you need to run them manually, use:

```bash
dotnet ef database update --project src/KinCare.API
```
