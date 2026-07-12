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

For the API service, add the production values for the secrets referenced in [src/KinCare.API/appsettings.Production.json](src/KinCare.API/appsettings.Production.json).

Minimum required values:
- `KINCARE_JWT_SECRET_KEY`
- `KINCARE_DB_CONNECTION_STRING` (Render will populate this automatically if the blueprint is used)
- `KINCARE_APP_BASE_URL` (set to the frontend URL after deployment)
- `TWILIO_ACCOUNT_SID`
- `TWILIO_AUTH_TOKEN`
- `TWILIO_FROM_NUMBER`
- `STRIPE_SECRET_KEY`
- `STRIPE_WEBHOOK_SECRET`
- `SENDGRID_API_KEY`
- `SENDGRID_FROM_EMAIL`

### 3. Frontend API URL

The frontend build reads `API_BASE_URL` during build and writes it into the production environment file.

If you deploy manually rather than with the blueprint, set the Render frontend build environment variable:
- `API_BASE_URL=https://<your-api-service>.onrender.com`

### 4. Database migration

The API now applies pending EF Core migrations automatically on startup. If you need to run them manually, use:

```bash
dotnet ef database update --project src/KinCare.API
```
