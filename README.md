# Personal home dashboard

A glanceable home dashboard for the household. It is built to be opened on tablets and laptops on
the home network now, and to run on a Raspberry Pi with a wall screen later.

## Tiles

| Tile | Data today | Planned source |
| --- | --- | --- |
| Weather: now, next 6 h (rain %, wind, UV), rest of today by hour, 7 days, umbrella/jacket hints | Mock | IPMA or Open-Meteo |
| Calendar × 2 (one per person in `Dashboard:People`) | Mock | Google Calendar / ICS |
| Next clients | Mock | Noona HQ API |
| Bus departures × 2 (one per stop in `Dashboard:BusStops`, with walk time) | Mock | STCP real-time |
| Drive to wife's work | Mock | Waze live-map routing (unofficial) or Google Routes API |
| Bank holidays | **Real**, computed in the API (PT national + municipal + Carnival) | — |
| Shopping list | **Real**, stored in `App_Data/shopping.json`, shared across devices | — |
| Photos slideshow | **Real** uploads, stored in `App_Data/photos/` (sample images until the first upload) | — |
| Parties we're attending | Mock | Calendar events tagged #party, or a list edited on the dashboard |
| What's on in the city | Mock | To be decided |

A "Sample data" badge in the header shows while `Dashboard:UseMockData` is `true`. Names, stops,
the commute label and the municipal holiday are all set in `src/Dashboard.Api/appsettings.json`.

There are no accounts: anyone on the network can edit the shopping list and upload or delete photos.
Keep the API reachable only on the LAN.

## Architecture

```
Browser (tablet / laptop / Pi kiosk)
        │  same origin
        ▼
nginx ─────────────► Angular SPA (static files)
  │ /api/*
  ▼
Dashboard.Api (.NET 10 minimal API)
  ├─ HybridCache: one upstream call per widget per interval, shared by every open screen
  └─ I{Widget}Provider ── Mock*Provider (today)
                       └─ real integrations (IPMA, Google Calendar, Noona, STCP, Waze…)
```

| Path | What it is |
| --- | --- |
| `src/Dashboard.Api` | .NET API. One folder per widget under `Features/` (records + provider interface + mock). |
| `src/Dashboard.Api/Infrastructure` | Provider registration, cached endpoint mapping, data folder, local clock and options. |
| `src/dashboard-web` | Angular 21 app (standalone, zoneless, signals). Tiles live in `src/app/tiles`. |
| `deploy/` | nginx site and systemd unit for the Pi. |

### Adding a real integration

1. Implement the widget's interface, e.g. `IpmaWeatherProvider : IWeatherProvider`, using a typed `HttpClient`.
2. Register it in `Infrastructure/ProviderRegistration.cs` instead of the mock.
3. Put any API keys in user secrets (dev) or `/etc/dashboard-api.env` (Pi), never in `appsettings.json`.

The frontend doesn't change: it only knows the JSON contract in `src/app/core/models.ts`.

### Adding a tile

1. Add `Features/<Name>/` in the API with records, an interface and a mock, then map it in `DashboardEndpoints.cs`.
2. Add the model to `models.ts`, a resource to `core/dashboard-data.ts`, and a component in `tiles/`.
3. Place it in the grid areas in `app.scss`. There are three layouts: 4 columns, 2 columns and 1 column.

Read resources with `valueOf()` from `shared/resource-state.ts`, never `.value()` directly. In Angular 21,
`.value()` throws while a resource is in the error state, and that breaks the whole page instead of one tile.

## Running locally

Prerequisites: .NET SDK 10 and Node 22.12 or later.

```bash
dotnet run --project src/Dashboard.Api --launch-profile http
```

```bash
npm --prefix src/dashboard-web install
```

```bash
npm --prefix src/dashboard-web start
```

Then open http://localhost:4300. The Angular dev server proxies `/api` to the API on port 5080
(`proxy.conf.json`).

To open the dashboard from a tablet on the same Wi-Fi, run `npm --prefix src/dashboard-web run start:lan`
and browse to `http://<your-pc-ip>:4300`. Windows may ask you to allow Node through the firewall.

> `src/dashboard-web/.npmrc` sets `legacy-peer-deps=true`. It works around an npm 10 bug
> ("Cannot read properties of null (reading 'edgesOut')") with vitest's optional jsdom/canvas peers.

## Deploying to a Raspberry Pi (later)

Use a Pi 4 (2 GB or more) or a Pi 5 with 64-bit Raspberry Pi OS. Build on your PC, not on the Pi:

```bash
npm --prefix src/dashboard-web run build
```

```bash
dotnet publish src/Dashboard.Api -c Release -r linux-arm64 --self-contained -o out/api
```

Then copy the files to the Pi and install the services:

- `src/dashboard-web/dist/dashboard-web/browser/` goes to `/var/www/dashboard/browser/`.
- `out/api/` goes to `/opt/dashboard-api/`. Its `App_Data/` folder (shopping list, photos) is created on first run, so back it up rather than overwriting it on redeploys.
- Install `deploy/nginx.conf` as an nginx site and `deploy/dashboard-api.service` as a systemd unit.
- For a wall screen, start Chromium in kiosk mode at boot: `chromium --kiosk --noerrdialogs http://localhost`.
