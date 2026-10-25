# Personal home dashboard

A glanceable home dashboard for the household. It is built to be opened on tablets and laptops on
the home network now, and to run on a Raspberry Pi with a wall screen later.

## Tabs

| Tab | What it's for |
| --- | --- |
| **Hoje** (`/`) | The daily glance: buses, clients, drive time, weather, calendars, photo slideshow, shopping summary, next trip, holidays, parties, city events |
| **Fotos** (`/fotos`) | Every uploaded photo, grouped in albums (moments and trips). Upload into an album, filter, and open a photo to edit its caption or album, or delete it |
| **Viagens** (`/viagens`) | Trips with a start date and an optional return ("volta em aberto"), an ordered itinerary of destinations (each with optional arrival/departure dates, how you get there from the previous one, and notes; reorder with ↑ / ↓), planning notes and a checklist (Comprar / Levar / Fazer). Stored in `App_Data/trips.json`; each trip links to its photo album |
| **Compras** (`/compras`) | Two views. **Lista de compras**: the items we usually buy (tap what ran out) and the next supermarket list with quantities and WhatsApp. **Estoque** (`?ver=estoque`): everything we have at home, with quantity, unit, place, optional minimum and expiry |

Navigation is a side rail on wide screens and a bottom bar on phones.

## Tiles

| Tile | Data today | Planned source |
| --- | --- | --- |
| Weather: now, next 6 h (rain %, wind, UV), rest of today by hour, 7 days, umbrella/jacket hints | **Real**: Open-Meteo, free, no key | — |
| Calendar × 2 (one per person in `Dashboard:People`) | **Real**: iCal links per person, several merged into one tile | — |
| Next clients | **Real** once the Noona OAuth app is configured and connected (see below). Sample data until configured | — |
| Bus departures × 2 (stops RMT3 and RMT2 in `Dashboard:BusStops`) | **Real**: STCP live arrivals (`stcp.pt/api/stops/{code}/realtime`, undocumented) | — |
| Drive to work | **Real** once `Dashboard:Commute:GoogleApiKey` is set: Google Routes API, traffic-aware. Sample data until then. Waze rejects server requests (403/410) | — |
| Bank holidays | **Real**, computed in the API (PT national + municipal + Carnival) | — |
| Shopping list: household items → next trip → WhatsApp | **Real**, stored in `App_Data/shopping.json`, shared across devices | — |
| Photos slideshow | **Real** uploads, stored in `App_Data/photos/` (sample images until the first upload) | — |
| Parties and festivals we're going to | **Real**: `App_Data/parties.json`, edited by hand | — |
| What's on in Porto and Gaia | **Real**: `App_Data/city-events.json`, edited by hand (no public feed covers the city agenda) | — |

`Dashboard:Integrations` in `src/Dashboard.Api/appsettings.json` picks the source for each widget
(`"Mock"` serves sample data). The header badge counts the tiles still on sample data. Names, stops,
the commute label and the municipal holiday are set in the same file.

Exact home coordinates go in `src/Dashboard.Api/appsettings.Local.json`, which is git-ignored and
overrides `appsettings.json`. Future API keys go there too:

```json
{ "Dashboard": { "Home": { "Name": "Vila Nova de Gaia", "Latitude": 41.12, "Longitude": -8.63 } } }
```

### Shopping list

- **Itens da casa** are the usual items, shown as chips. Tap one when it runs out, and it moves to
  **Próxima compra**, with a − / + quantity (whole number, starting at 1). The WhatsApp message shows
  "2× Leite" when the quantity is above 1, and the count goes back to 1 when the item leaves the list. Type in the box to search (accents don't matter); **+** or
  Enter adds a new item, by name only, to the household list.
- **Enviar para WhatsApp** opens WhatsApp (the app on phones and tablets, WhatsApp Web on a PC) with
  the list ready to send. Nothing is sent automatically. Set `Dashboard:Shopping:WhatsAppNumber` in
  `appsettings.Local.json` (international format, e.g. `351912345678`) to go straight to that chat.
  Leave it empty to pick a chat or group each time.
- **Compras feitas** empties the trip list and keeps the household items. **Editar** lets you remove
  household items.

### Parties

`App_Data/parties.json` holds our agenda. Dates are local (`yyyy-MM-dd`); `end` is for multi-day
festivals, and `time` (`HH:mm`), `location`, `host`, `bring` and `dressCode` are optional. Past events
drop off by themselves, and edits show up within a minute.

```json
{ "parties": [
  { "title": "Tomorrowland Brasil", "start": "2027-04-30", "end": "2027-05-02", "location": "Parque Maeda, Itu (SP)" },
  { "title": "Aniversário", "start": "2026-11-07", "time": "20:00", "location": "Casa da Joana", "bring": "Sobremesa" }
] }
```

### City events

`App_Data/city-events.json` is created on first run with examples in comments. It uses the same date
rules as `parties.json`, plus optional `venue`, `category` (Música, Exposição, Feira, Teatro, Gastronomia…
each has its own colour), `price` and `url` (makes the card a link). I checked Porto open data,
porto.pt, Câmara de Gaia, Casa da Música, RA and Viral Agenda, and none has a usable public events feed.

### Stock (Estoque)

`App_Data/stock.json`, edited from Compras → Estoque. Items are grouped by place (Despensa, Geladeira,
Congelador, Limpeza, Banheiro, or any place you type). − / + adjust quantities (by 0.5 for kg and L).
An item is flagged **Acabando** at or below its minimum, **Acabou** at 0, and **Vence em N d** / **Vencido**
near its expiry. The cart button puts it on the next shopping list. **Compras feitas** adds what was
bought to stock items with the same name. Adding an item that already exists in the same place adds
to its quantity.

### Calendars (iCal links)

Each person's tile merges any number of iCal feeds, e.g. personal and work. Repeating events are
expanded, cancelled ones dropped, and an event that's in two feeds is shown once. Put the links in
`appsettings.Local.json`, because a private iCal link is a password:

```json
"Calendars": {
  "daniel": [
    { "Name": "Pessoal",  "Url": "https://calendar.google.com/calendar/ical/.../private-.../basic.ics" },
    { "Name": "Trabalho", "Url": "https://...", "BusyOnly": true }
  ],
  "leesy": [ { "Name": "Pessoal", "Url": "https://..." } ]
}
```

- **Google:** Settings → your calendar → *Integrate calendar* → **Secret address in iCal format**.
  The *public* address only works if the calendar is public to everyone, and returns 404 otherwise.
- **Outlook / Microsoft 365:** Settings → Calendar → Shared calendars → *Publish a calendar* → ICS link.
- **iPhone (iCloud):** Calendar app → *Calendars* → ⓘ next to an iCloud calendar → turn on *Public Calendar*
  → *Share Link…*. The `webcal://` link works as is; anyone with it can read the calendar, so keep it private.
- `BusyOnly` shows "Ocupado" instead of titles. A feed that fails is named on the tile, and the
  others still show. Feed URLs are never logged.

### Noona (salon clients)

Noona's HQ API only supports OAuth 2.0, so this is a one-time connect rather than a key:

1. In the [Noona Developer Portal](https://developers.noona.app/), create an app while logged in with a
   Noona HQ account that has access to the salon.
2. Set its redirect URL to `http://localhost:4300/api/noona/callback` (the dashboard's own origin;
   on the Pi, add `http://<pi-address>/api/noona/callback` and set `Dashboard:Noona:RedirectUri` to match).
3. Under **Scopes**, select `events:read` and `event_types:read` (`user:read` and `companies:read` are
   mandatory) and click **Save**. Until you save, Noona rejects every scope.
4. Put the Client ID and Secret in `appsettings.Local.json` under `Dashboard:Noona`, then restart the API.
5. On the computer running the dashboard, click **Conectar ao Noona** on the clients tile and log in.
   The API stores the tokens encrypted (ASP.NET Data Protection) in `App_Data/noona-tokens.bin`,
   renews them automatically, and finds the company from the logged-in user (override with
   `Dashboard:Noona:CompanyId`; filter to one person with `Dashboard:Noona:EmployeeId`).

The tile shows the next bookings in the coming 8 days. Client names are shortened to first name + initial
on screen unless `Dashboard:Noona:ShowFullNames` is `true`.
`POST /api/noona/disconnect` forgets the tokens.

### Google Routes (drive time)

1. In Google Cloud, enable the **Routes API** on a project with billing, then create an API key.
   Restrict the key to the Routes API only.
2. Put the key in `appsettings.Local.json` under `Dashboard:Commute:GoogleApiKey` and restart the API.
3. Each request is billed. The server caches the drive time for 10 minutes between 06:00 and 22:00
   and for 60 minutes overnight (`RefreshMinutes`, `NightRefreshMinutes`). That is about 100 requests
   a day however many screens are open. To be safe, also set a daily quota cap on the Routes API in
   the Cloud console.
4. "Usual" is the median of our own past readings for that hour and kind of day, kept in
   `App_Data/commute-history.json`. Until there are a few days of readings, the tile compares against
   the no-traffic time instead.

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
