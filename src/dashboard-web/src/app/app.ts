import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { Clock } from './core/clock';
import { DashboardData } from './core/dashboard-data';
import { Theme } from './core/theme';
import { valueOf } from './shared/resource-state';
import { Icon, IconName, weatherIcon } from './shared/icon';
import { AppointmentsTile } from './tiles/appointments-tile';
import { BusTile } from './tiles/bus-tile';
import { CalendarTile } from './tiles/calendar-tile';
import { CityEventsTile } from './tiles/city-events-tile';
import { CommuteTile } from './tiles/commute-tile';
import { HolidaysTile } from './tiles/holidays-tile';
import { PartiesTile } from './tiles/parties-tile';
import { PhotosTile } from './tiles/photos-tile';
import { ShoppingTile } from './tiles/shopping-tile';
import { WeatherTile } from './tiles/weather-tile';

@Component({
  selector: 'app-root',
  imports: [
    DecimalPipe,
    Icon,
    WeatherTile,
    CalendarTile,
    AppointmentsTile,
    BusTile,
    CommuteTile,
    PhotosTile,
    ShoppingTile,
    HolidaysTile,
    PartiesTile,
    CityEventsTile,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  protected readonly clock = inject(Clock);
  protected readonly theme = inject(Theme);
  private readonly data = inject(DashboardData);

  protected readonly config = computed(() => valueOf(this.data.config));
  protected readonly people = computed(() => (this.config()?.people ?? []).slice(0, 2));
  protected readonly stops = computed(() => (this.config()?.busStops ?? []).slice(0, 2));
  protected readonly weather = computed(() => valueOf(this.data.weather));
  protected readonly weatherIcon = weatherIcon;

  /** Header badge: which tiles still show sample data, in words the family understands. */
  protected readonly mockSummary = computed(() => {
    const names: Record<string, string> = {
      weather: 'Tempo', transit: 'Ônibus', commute: 'Trajeto', calendar: 'Calendários',
      appointments: 'Clientes', cityEvents: 'Eventos', parties: 'Festas',
    };
    const mocks = this.config()?.mockWidgets ?? [];
    return mocks.length ? { count: mocks.length, list: mocks.map((m) => names[m] ?? m).join(', ') } : null;
  });
  protected readonly themeIcon = computed<IconName>(() =>
    ({ auto: 'themeAuto', light: 'sun', dark: 'moon' } as const)[this.theme.mode()],
  );
  protected readonly themeLabel = computed(() => ({ auto: 'automático', light: 'claro', dark: 'escuro' })[this.theme.mode()]);
}
