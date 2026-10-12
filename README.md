# Project ROSE API

The backend for [Project ROSE](https://github.com/chupipings1424/project-rose): a .NET 9 ASP.NET Core
Web API with Swagger, serving the **Payment Tracker**, **Personal Budget** and **Reminders** projects.

ROSE (the React app) is only the UI. This API owns the data:

| Project | Storage |
| --- | --- |
| Payment Tracker + Personal Budget | `data/payments.xlsx` - the Excel workbook *is* the database |
| Reminders | `data/reminders.json`, `data/push-devices.json`, `data/vapid.json` |

`data/` is git-ignored - your personal data never leaves your PC.

## Architecture

A clean, layered solution. Dependencies point inwards only.

```
src/
  Rose.Domain          pure rules: schedules, budget maths, reports, reminder repeats (no I/O)
  Rose.Application     use cases (services) + interfaces for the outside world
  Rose.Infrastructure  Excel (ClosedXML), JSON files, CSV/XLSX import, Web Push, desktop alarm
  Rose.Api             controllers, auth (JWT), Swagger, the reminder background worker
tests/
  Rose.UnitTests            domain + services + real temp workbooks
  Rose.Api.IntegrationTests the whole API in memory (WebApplicationFactory)
```

## Run it

Prerequisite: the [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).

1. **Set your access token** (the one you type on the ROSE login page). Either:
   - copy `src/Rose.Api/appsettings.Local.example.json` to `src/Rose.Api/appsettings.Local.json`
     and put your token in it (that file is git-ignored), **or**
   - use user-secrets:
     ```powershell
     dotnet user-secrets --project src/Rose.Api set "Auth:AccessToken" "your-token"
     ```
2. **Put your data in `data/`** (optional - an empty workbook is created on first use):
   `payments.xlsx`, `reminders.json`, `push-devices.json`, `vapid.json`.
3. **Start it:**
   ```powershell
   dotnet run --project src/Rose.Api
   ```
   It listens on <http://127.0.0.1:5175> and opens **Swagger** at <http://127.0.0.1:5175/swagger>.

From the ROSE repo, `npm start` starts this API *and* the UI together.

### Try it in Swagger

1. Open `POST /api/auth/login` -> **Try it out** -> `{ "token": "your-token" }` -> **Execute**.
2. Copy the `access_token` from the response.
3. Click **Authorize** (top right), paste it, **Authorize**. Every other endpoint now works.

## Configuration

`src/Rose.Api/appsettings.json` (override with `appsettings.Local.json`, user-secrets or environment
variables such as `Reminders__DesktopAlarms=false`):

| Key | Default | Meaning |
| --- | --- | --- |
| `Urls` | `http://127.0.0.1:5175` | Where the API listens (localhost only) |
| `Rose:DataDirectory` | `data` (`../../data` in Development) | Folder with your data files |
| `Rose:WorkbookPath` | `<DataDirectory>/payments.xlsx` | Use a workbook somewhere else |
| `Auth:AccessToken` | *(required)* | The login token |
| `Auth:SessionLifetime` | `12:00:00` | How long a login lasts |
| `Reminders:SchedulerEnabled` | `true` | Ring reminders in the background |
| `Reminders:DesktopAlarms` | `true` | Always-on-top popup on this PC |
| `Reminders:TickInterval` | `00:00:15` | How often due reminders are checked |
| `Shutdown:Enabled` | `false` | Let ROSE close the API (last tab closed, or the power button). The ROSE launcher sets it to `true` |
| `Shutdown:IdleTimeout` | `00:00:20` | How long to wait after the last ROSE page disconnected |

### Auto-close

With `Shutdown:Enabled`, every open ROSE page holds `GET /api/system/presence` (a Server-Sent
Events stream). When the last stream has been gone for `IdleTimeout`, a background service
(`ShutdownWatchdog`) stops the API. The ROSE launcher (`npm start` / `start-rose.bat`) then
stops the UI too. `dotnet run` and Visual Studio leave it off, so the API keeps running.

## Security

- The access token is compared in constant time; `POST /api/auth/login` allows 10 tries a minute.
- A login returns a signed JWT (HS256). The signing key is generated once into
  `data/session-signing.key` - delete it to log every device out.
- Every endpoint except `/health`, login and `/api/system/*` needs that session token.
- `/api/system/*` is anonymous because the power button is also on the login page. It is
  harmless: `POST /api/system/shutdown` requires an `X-Rose-Client` header. A custom header
  forces a CORS preflight, which this API never grants, so other websites cannot call it.
- Secrets are never committed: `appsettings.Local.json` and `data/` are git-ignored.

## Test

```powershell
dotnet test
```

Unit tests cover the domain rules (including daylight-saving changes), services and real temp
workbooks; integration tests run the whole API in memory with a temp data folder.
The port was also checked response-for-response against the original Flask and Node servers.

## API overview

| Area | Endpoints |
| --- | --- |
| Auth | `POST /api/auth/login`, `GET /api/auth/session` |
| Payment Tracker | `/api/payment-tracker/` `accounts`, `summary/{title}`, `payments`, `budget`, `report/summary`, `import/*`, `download`, `home-stats` |
| Reminders | `/api/reminders/` `status`, `items`, `items/{id}/complete\|dismiss\|snooze`, `push/key\|subscribe\|unsubscribe\|test` |
| System | `GET /api/system/info`, `GET /api/system/presence`, `POST /api/system/shutdown` |
| Health | `GET /health` |

Full, interactive documentation: Swagger.