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
    venue: string;
    start: string;
    end: string | null;
    category: string;
    price: string | null;
    url: string | null;
  }[];
  updatedAt: string;
}

export interface PartyList {
  parties: { title: string; host: string; start: string; location: string; bring: string | null; dressCode: string | null }[];
  updatedAt: string;
}

export interface ShoppingItem {
  id: string;
  name: string;
  quantity: string | null;
  done: boolean;
  addedAt: string;
}

export interface ShoppingList {
  items: ShoppingItem[];
  updatedAt: string;
}

export interface Photo {
  id: string;
  url: string;
  sample: boolean;
  addedAt: string;
}

export interface PhotoList {
  photos: Photo[];
  rotateSeconds: number;
}
