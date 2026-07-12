# KinCare Local Development

## Quick start with Docker Compose

The fastest way to run the full stack locally is to start the database, API, and Angular frontend together:

```bash
cd /Users/jozy/Downloads/KinCare 2
docker compose up --build
```

Then open:
- Frontend: http://localhost:4200
- API: http://localhost:8080
- Swagger UI: http://localhost:8080/swagger/index.html

To stop everything:

```bash
docker compose down
```

## Prerequisites

- Docker Desktop with Docker Compose
- PostgreSQL is started automatically by the compose stack

## Local API only

If you want to run the API outside Docker:

```bash
cd /Users/jozy/Downloads/KinCare 2
dotnet run --project src/KinCare.API
```

Make sure the development config exists and points to the local database:

```bash
cp src/KinCare.API/appsettings.Development.json.example src/KinCare.API/appsettings.Development.json
```

The development config is ignored by Git and should not be committed.

## Local Angular frontend only

```bash
cd /Users/jozy/Downloads/KinCare 2/src/KinCare.Web
npm install
npm start
```

The frontend will be available at http://localhost:4200 and will call the API at http://localhost:8080.
