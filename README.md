# Project ROSE API

The backend for [Project ROSE](https://github.com/chupipings1424/project-rose): a .NET 9 ASP.NET Core
Web API serving the **Payment Tracker**, **Personal Budget**, **Reminders**, **Savings Goals** and
**Habit Tracker** projects. In the cloud it also serves the built React app, so ROSE is one website.

```
Browser / installed PWA ??HTTPS??> Render (free, Singapore): Docker container
                                     ?? React app (wwwroot)
                                     ?? Rose.Api ??> Supabase Postgres (free, Singapore)
Supabase Auth: email + password -> access token (JWT) -> checked by Rose.Api on every request
Brevo: reminder and habit emails (HTTPS API; Render's free plan blocks SMTP)
cron-job.org: GET /health every 10 minutes (keeps Render and Supabase awake)
GitHub Actions: weekly encrypted pg_dump into a private repo
```

## Architecture

A clean, layered solution. Dependencies point inwards only.

```
src/
  Rose.Domain          pure rules: schedules, budget maths, reports, reminder repeats,
                       savings pace/status, habit streaks and nudge timing (no I/O)
  Rose.Application     use cases (services) + interfaces (IRepository<T>, ITrackerWorkbookStore...)
  Rose.Infrastructure  Persistence/ (EF Core + Npgsql: RoseDbContext, repositories, migrations),
                       Excel/ (ClosedXML export/import), Importing/ (CSV/XLSX), Reminders/ (Web Push)
  Rose.Api             controllers, Supabase JWT auth + ROSE accounts (admin page), Swagger (Development),
                       the background scheduler (reminders, habit nudges), static React app
tests/
  Rose.UnitTests            domain + services (in-memory fakes)
  Rose.Api.IntegrationTests the whole API in memory against a real Postgres
```

All tables live in the `rose` schema (snake_case). On startup the API applies pending EF
migrations and enables row-level security on every `rose` table with no policies, so
Supabase's public Data API cannot read them - only the API's own connection can.

## Run it locally

Prerequisites: the [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) and
[Docker Desktop](https://www.docker.com/products/docker-desktop/) (for the local database).

1. **Start Postgres** (port 5433, data in the `rose-pg` Docker volume):
   ```powershell
   docker compose up -d --wait
   ```
2. **Point the API at your Supabase project** (login still goes through Supabase): copy
   `src/Rose.Api/appsettings.Local.example.json` to `src/Rose.Api/appsettings.Local.json`
   (git-ignored) and fill in the URL, publishable key and your email (as admin). The secret key
   and Brevo settings are optional locally: without them adding accounts answers 503 and no
   emails are sent.
3. **Start the API:**
   ```powershell
   dotnet run --project src/Rose.Api
   ```
   It listens on <http://127.0.0.1:5175>; Swagger is at <http://127.0.0.1:5175/swagger>.

From the ROSE UI repo, `npm start` does all three steps (and starts Vite) for you.

### Try it in Swagger

Sign in on the ROSE login page, open the browser dev tools -> Application -> Local Storage
-> `rose.supabase`, copy `access_token`, click **Authorize** in Swagger and paste it.

### Database migrations

The schema is code-first. After changing `RoseDbContext` or `Rows.cs`:

```powershell
dotnet tool restore
dotnet ef migrations add <Name> --project src/Rose.Infrastructure --output-dir Persistence/Migrations
```

The next start applies it (locally and on Render).

## Configuration

`appsettings.json`, then `appsettings.{Environment}.json`, then `appsettings.Local.json`, then
user-secrets, then **environment variables** (`__` = `:`, e.g. `Supabase__Url`) - later wins.

| Key | Default | Meaning |
| --- | --- | --- |
| `ConnectionStrings:Rose` | `localhost:5433` in Development | Postgres connection string (*required*) |
| `Supabase:Url` | *(required)* | `https://<project-ref>.supabase.co` |
| `Supabase:PublishableKey` | | Public key the React app signs in with (served by `GET /api/config`) |
| `Supabase:SecretKey` | | `sb_secret_...` key; lets the admin page create, reset and delete Supabase logins. **Secret** |
| `Access:AdminEmails` | *(required, 1+)* | Admins: manage accounts on the Users page. The first admin sign-in takes over data from before accounts existed |
| `Email:BrevoApiKey` | | Brevo API key. Empty = no emails |
| `Email:FromAddress` / `Email:FromName` | / `ROSE` | Sender (must be a verified sender in Brevo) |
| `Email:AppUrl` | `RENDER_EXTERNAL_URL` | Link put in emails |
| `Supabase:JwtSecret` | | Only for legacy HS256 projects; new projects use the JWKS public keys |
| `Reminders:SchedulerEnabled` | `true` | Ring reminders and send habit nudges in the background |
| `Reminders:TickInterval` | `00:00:15` | How often due reminders and habit nudges are checked |
| `Reminders:PushContact` | repo URL | Contact sent to push services (VAPID subject) |
| `Push:VapidPublicKey` / `Push:VapidPrivateKey` | | Optional; otherwise generated once and stored in `rose.app_secrets` |

## Security

- Login is Supabase's job. The API only accepts Supabase access tokens: signature (JWKS),
  issuer, audience `authenticated`, expiry and `role=authenticated` are checked, then the
  token's user id must be a ROSE account (otherwise 403). Admins become accounts on first sign-in;
  everybody else is added by an admin with a temporary password and gets 403 everywhere except
  `/api/auth/*` until they choose their own.
- **Every row has an `owner_id`** (the Supabase user id). Repositories and the tracker store
  always filter by it, so one account can never read, change or download another's data.
  Removing an account deletes its login and all of its data in one transaction.
- Anonymous endpoints: `/health` and `GET /api/config` (public URL + publishable key only).
- Sign-ups should be disabled in Supabase (see the deploy guide), so nobody else can even get a token.
- Row-level security locks the `rose` schema away from Supabase's Data API.
- Responses carry `nosniff`, `X-Frame-Options: DENY` and `Referrer-Policy: no-referrer`;
  Swagger is only exposed in Development.
- Secrets are never committed: they live in Render env vars, GitHub secrets and the
  git-ignored `appsettings.Local.json`.

## Test

```powershell
dotnet test
```

Unit tests need nothing. Integration tests start `postgres:17-alpine` with Testcontainers
(Docker must be running) and give every test class a fresh database. To use a Postgres you
already have instead, set `ROSE_TEST_POSTGRES`, e.g.
`$env:ROSE_TEST_POSTGRES="Host=127.0.0.1;Port=5433;Username=rose;Password=rose"`.
Test tokens are minted locally with a test-only HS256 secret.

## API overview

| Area | Endpoints |
| --- | --- |
| Auth | `GET /api/config` (public), `GET /api/auth/session`, `POST /api/auth/password` |
| Admin | `/api/admin/users` `GET`, `POST`, `POST {id}/password`, `DELETE {id}` |
| Payment Tracker | `/api/payment-tracker/` `accounts`, `summary/{title}`, `payments`, `budget`, `report/summary`, `import/*`, `download`, `home-stats` |
| Reminders | `/api/reminders/` `status`, `items`, `items/{id}/complete\|dismiss\|snooze`, `push/key\|devices\|subscribe\|unsubscribe\|test` |
| Savings Goals | `/api/savings/` `goals`, `goals/{id}`, `goals/{id}/entries`, `goals/{id}/entries/{entryId}` |
| Habit Tracker | `/api/habits/` `items`, `items/{id}`, `PUT items/{id}/days/{yyyy-MM-dd}`, `items/{id}/nudge/complete\|dismiss\|snooze` |
| Health | `GET /health` (includes a database check) |

`GET /api/payment-tracker/download` still gives you everything as an Excel workbook.

## Deploy (all free)

The `Dockerfile` builds the React app from `chupipings1424/project-rose` (`UI_REF`, default
`main`), publishes the API, and runs both in one `aspnet:9.0` container (`TZ=Asia/Manila`).

### 1. Supabase (database + login)

1. Sign up at <https://supabase.com> -> **New project**: name `rose`, region **Southeast Asia
   (Singapore)**, generate a strong database password and keep it in your password manager.
2. **Authentication -> Sign In / Providers**: keep **Email** on, turn **Allow new users to sign
   up** off.
3. **Authentication -> Users -> Add user -> Create new user**: your email + a strong password,
   tick **Auto Confirm User**.
4. **Project Settings -> API Keys**: copy the **Project URL**, the **publishable** key and a
   **secret** key (`sb_secret_...`, never share it).
5. **Connect** (top bar) -> **Session pooler** (IPv4) and build an Npgsql connection string:
   `Host=aws-0-ap-southeast-1.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.<project-ref>;Password=<db password>;SSL Mode=Require`
   (use the host shown on that page).

### 2. Render (hosting)

1. Push the `main` branches of both repos.
2. Sign up at <https://render.com> with GitHub -> **New -> Blueprint** -> pick `project-rose-api`.
   `render.yaml` creates the free Singapore web service and asks for:
   `ConnectionStrings__Rose`, `Supabase__Url`, `Supabase__PublishableKey`, `Supabase__SecretKey`,
   `Access__AdminEmails__0` (your email), `Email__BrevoApiKey`, `Email__FromAddress`.
3. Wait for the first deploy, then open `https://<service>.onrender.com/health` -> `Healthy`.
4. **Settings -> Deploy Hook**: copy the URL into the UI repo as the Actions secret
   `RENDER_DEPLOY_HOOK`, so UI changes on `main` redeploy too (API pushes deploy automatically).

### Emails (Brevo, free 300/day)

1. Sign up at <https://www.brevo.com>, then **Senders, Domains & Dedicated IPs -> Senders ->
   Add a sender** with your email and confirm the link they mail you.
2. **SMTP & API -> API Keys -> Generate a new API key**; put it in `Email__BrevoApiKey` and the
   sender in `Email__FromAddress`.
3. Reminders and habit nudges are emailed on their first ring (not on snooze re-rings). Check
   spam the first time and mark it *Not spam*.

### Accounts

Sign in once as the admin after deploying (that claims your existing data), then open
**Users** in ROSE to add someone: give them the temporary password; they must choose their own
on first sign-in. Push notifications only go to devices where *Enable notifications* was
turned on.

### 3. cron-job.org (stay awake)

Render's free plan sleeps after 15 idle minutes, which would stop reminders; Supabase pauses
projects after a week without activity. Create a free job at <https://cron-job.org>:
URL `https://<service>.onrender.com/health`, every **10 minutes**.

### 4. Weekly backup

1. Create a **private** repo `rose-backups` and copy `ops/backup/rose-backup.yml` into its
   `.github/workflows/`.
2. Add the Actions secrets `ROSE_DB_URL` (the session-pooler URI from Supabase **Connect**, in
   `postgresql://...` form) and `BACKUP_PASSPHRASE` (a long random passphrase - keep a copy
   in your password manager; without it the backups are unreadable).
3. **Actions -> ROSE backup -> Run workflow** once to check it. It runs every Sunday 02:00
   Manila time and keeps the newest 12 files.

Restore: `gpg -d rose-YYYY-MM-DD.sql.gz.gpg | gunzip | psql "<connection uri>"`.

### 5. Install ROSE

Open the Render URL and sign in. **Edge/Chrome**: the install icon in the address bar.
**iPhone (Safari)**: Share -> Add to Home Screen (needed for push notifications on iOS).
