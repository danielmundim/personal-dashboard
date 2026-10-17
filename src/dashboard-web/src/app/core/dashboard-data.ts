import { Injectable } from '@angular/core';
import { httpResource } from '@angular/common/http';
import { pollingResource } from './polling-resource';
import {
  AppointmentList,
  CityEventList,
  DashboardConfig,
  DriveEstimate,
  HolidayList,
  PartyList,
  WeatherReport,
} from './models';

export const SECOND = 1000;
export const MINUTE = 60 * SECOND;

/**
 * One shared resource per single-instance widget, so the header strip and the tiles reuse the same
 * request. Per-person and per-stop widgets (calendar, bus) create their own resource from an input.
 * Intervals sit a little above the server cache TTLs in DashboardEndpoints.cs.
 */
@Injectable({ providedIn: 'root' })
export class DashboardData {
  readonly config = httpResource<DashboardConfig>(() => '/api/config');
  readonly weather = pollingResource<WeatherReport>('/api/weather', 10 * MINUTE);
  readonly holidays = pollingResource<HolidayList>('/api/holidays', 60 * MINUTE);
  readonly appointments = pollingResource<AppointmentList>('/api/appointments', 2 * MINUTE);
  readonly commute = pollingResource<DriveEstimate>('/api/commute', 2 * MINUTE);
  readonly cityEvents = pollingResource<CityEventList>('/api/city-events', 60 * MINUTE);
  readonly parties = pollingResource<PartyList>('/api/parties', 10 * MINUTE);
}
