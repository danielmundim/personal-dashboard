// Mirrors the records in Dashboard.Api/Features. Dates arrive as ISO strings; enums as camelCase strings.

export type Accent = 'sky' | 'amber' | 'green' | 'violet' | 'rose' | 'teal';

export interface DashboardConfig {
  locationName: string;
  timeZone: string;
  /** Widgets still served from sample data (see Dashboard:Integrations in the API). */
  mockWidgets: string[];
  people: { id: string; name: string; color: Accent }[];
  busStops: { id: string; name: string; code: string; walkMinutes: number }[];
}

export type WeatherCondition =
  | 'clear'
  | 'partlyCloudy'
  | 'cloudy'
  | 'fog'
  | 'drizzle'
  | 'rain'
  | 'thunderstorm';

export interface HourlyForecast {
  time: string;
  temperatureC: number;
  precipitationPct: number;
  precipitationMm: number;
  windKmh: number;
  uvIndex: number;
  condition: WeatherCondition;
}

export interface DailyForecast {
  date: string;
  minC: number;
  maxC: number;
  precipitationPct: number;
  precipitationMm: number;
  windMaxKmh: number;
  uvMax: number;
  condition: WeatherCondition;
}

export interface WeatherReport {
  location: string;
  now: {
    temperatureC: number;
    feelsLikeC: number;
    condition: WeatherCondition;
    description: string;
    humidityPct: number;
    windKmh: number;
    windGustKmh: number;
    windDirection: string;
    uvIndex: number;
  };
  hints: { kind: 'umbrella' | 'jacket' | 'sunscreen' | 'wind'; text: string }[];
  hourly: HourlyForecast[];
  daily: DailyForecast[];
  sun: { sunrise: string; sunset: string; daylightLength: string; daylightChangeVsYesterday: string };
  updatedAt: string;
}

export interface Holiday {
  name: string;
  date: string;
  scope: 'national' | 'municipal' | 'optional';
  daysUntil: number;
  longWeekend: boolean;
  bridgeDay: boolean;
}

export interface HolidayList {
  country: string;
  holidays: Holiday[];
  updatedAt: string;
}

export interface CalendarEvent {
  title: string;
  start: string;
  end: string;
  allDay: boolean;
  location: string | null;
  /** Feed name, e.g. "Pessoal" or "Trabalho". */
  source: string | null;
  /** Busy-only feed: title is "Ocupado" and there's no location. */
  busy: boolean;
}

export interface CalendarAgenda {
  personId: string;
  events: CalendarEvent[];
  updatedAt: string;
  /** False when this person has no calendar links configured yet. */
  configured: boolean;
  /** Feeds that couldn't be read on the last refresh. */
  failedSources: string[] | null;
}

export interface AppointmentList {
  appointments: {
    customer: string;
    service: string;
    start: string;
    durationMinutes: number;
    status: 'confirmed' | 'unconfirmed' | 'checkedIn';
    firstVisit: boolean;
    note: string | null;
  }[];
  todayCount: number;
  todayRemaining: number;
  updatedAt: string;
}

export interface StopBoard {
  stopId: string;
  stopName: string;
  stopCode: string;
  walkMinutes: number;
  lines: string[];
  departures: {
    line: string;
    destination: string;
    minutesAway: number;
    realTime: boolean;
    delayed: boolean;
    /** Best estimate (live when available); the countdown is computed from this every second. */
    arrivesAt: string | null;
    /** Timetable time, to compare with other apps. */
    scheduledAt: string | null;
    delayMinutes: number;
    color: string | null;
    textColor: string | null;
  }[];
  updatedAt: string;
}

export interface DriveEstimate {
  label: string;
  destination: string;
  currentMinutes: number;
  usualMinutes: number;
  /** history = our median for this hour; freeFlow = no-traffic time until there is enough history. */
  usualSource: 'history' | 'freeFlow';
  distanceKm: number;
  via: string;
  traffic: 'light' | 'moderate' | 'heavy';
  arriveIfLeavingNow: string;
  today: { hour: string; minutes: number }[];
  incident: string | null;
  updatedAt: string;
}

export interface CityEventList {
  city: string;
  source: string;
  events: {
    title: string;
    venue: string | null;
    start: string;
    /** Last day of multi-day events (exhibitions, fairs). */
    end: string | null;
    /** No start time given: show the date only. */
    allDay: boolean;
    category: string | null;
    price: string | null;
    url: string | null;
  }[];
  updatedAt: string;
}

export interface Party {
  title: string;
  start: string;
  /** Last day for multi-day events (festivals); null for a single evening. */
  end: string | null;
  /** No time given: show dates only. */
  allDay: boolean;
  location: string | null;
  host: string | null;
  bring: string | null;
  dressCode: string | null;
}

export interface PartyList {
  parties: Party[];
  updatedAt: string;
}

/** A usual household item; `needed` puts it on the next supermarket trip, `quantity` (≥ 1) is how many. */
export interface ShoppingItem {
  id: string;
  name: string;
  quantity: number;
  needed: boolean;
  addedAt: string;
  neededSince: string | null;
}

export interface ShoppingList {
  items: ShoppingItem[];
  neededCount: number;
  whatsAppNumberSet: boolean;
  updatedAt: string;
}

export interface WhatsAppMessage {
  url: string;
  text: string;
  itemCount: number;
}

export interface Photo {
  id: string;
  url: string;
  sample: boolean;
  addedAt: string;
  /** Moment or trip the photo belongs to, e.g. "Lisboa 2026". */
  album: string | null;
  caption: string | null;
}

export interface AlbumSummary {
  name: string;
  count: number;
  coverUrl: string;
}

export interface PhotoList {
  photos: Photo[];
  albums: AlbumSummary[];
  rotateSeconds: number;
}

export type StockStatus = 'ok' | 'low' | 'out';

export interface StockItem {
  id: string;
  name: string;
  quantity: number;
  unit: string;
  /** Where it's kept: Despensa, Geladeira, Congelador, … */
  location: string;
  minQuantity: number | null;
  /** "yyyy-mm-dd" */
  expiresOn: string | null;
  updatedAt: string;
}

export interface StockView {
  item: StockItem;
  status: StockStatus;
  daysToExpiry: number | null;
}

export interface StockList {
  items: StockView[];
  locations: string[];
  lowCount: number;
  expiringCount: number;
  updatedAt: string;
}

export interface SaveStockItem {
  name: string;
  quantity: number;
  unit: string | null;
  location: string | null;
  minQuantity: number | null;
  expiresOn: string | null;
}

export type ChecklistKind = 'comprar' | 'levar' | 'fazer';

export interface ChecklistItem {
  id: string;
  text: string;
  kind: ChecklistKind;
  done: boolean;
}

export interface Trip {
  id: string;
  name: string;
  destination: string | null;
  /** Local calendar days, "yyyy-mm-dd". */
  startDate: string;
  endDate: string | null;
  notes: string | null;
  checklist: ChecklistItem[];
  createdAt: string;
}

export interface TripList {
  upcoming: Trip[];
  past: Trip[];
  updatedAt: string;
}

export interface SaveTrip {
  name: string;
  destination: string | null;
  startDate: string;
  endDate: string | null;
  notes: string | null;
}
