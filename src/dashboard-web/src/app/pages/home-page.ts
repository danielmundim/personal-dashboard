import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { DashboardData } from '../core/dashboard-data';
import { valueOf } from '../shared/resource-state';
import { AppointmentsTile } from '../tiles/appointments-tile';
import { BusTile } from '../tiles/bus-tile';
import { CalendarTile } from '../tiles/calendar-tile';
import { CityEventsTile } from '../tiles/city-events-tile';
import { CommuteTile } from '../tiles/commute-tile';
import { HolidaysTile } from '../tiles/holidays-tile';
import { PartiesTile } from '../tiles/parties-tile';
import { PhotosTile } from '../tiles/photos-tile';
import { ShoppingSummaryTile } from '../tiles/shopping-summary-tile';
import { TripSummaryTile } from '../tiles/trip-summary-tile';
import { WeatherTile } from '../tiles/weather-tile';

/** Hoje: the daily glance. Each subject with more to do (fotos, viagens, compras) has its own tab. */
@Component({
  selector: 'app-home-page',
  imports: [
    WeatherTile,
    CalendarTile,
    AppointmentsTile,
    BusTile,
    CommuteTile,
    PhotosTile,
    ShoppingSummaryTile,
    TripSummaryTile,
    HolidaysTile,
    PartiesTile,
    CityEventsTile,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-weather-tile class="area-weather" />
    @for (p of people(); track p.id; let i = $index) {
      <app-calendar-tile [class]="'area-cal' + i" [person]="p" />
    }
    @for (s of stops(); track s.id; let i = $index) {
      <app-bus-tile [class]="'area-bus' + i" [stop]="s" />
    }
    <app-appointments-tile class="area-clients" />
    <app-commute-tile class="area-drive" />
    <app-photos-tile class="area-photos" />
    <app-shopping-summary-tile class="area-shopping" />
    <app-trip-summary-tile class="area-trip" />
    <app-holidays-tile class="area-holidays" />
    <app-parties-tile class="area-parties" />
    <app-city-events-tile class="area-events" />
  `,
  styleUrl: './home-page.scss',
})
export class HomePage {
  private readonly data = inject(DashboardData);
  private readonly config = computed(() => valueOf(this.data.config));
  protected readonly people = computed(() => (this.config()?.people ?? []).slice(0, 2));
  protected readonly stops = computed(() => (this.config()?.busStops ?? []).slice(0, 2));
}
