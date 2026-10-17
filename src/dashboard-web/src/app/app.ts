import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { Clock, fmt, inDays } from './core/clock';
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

interface Glance {
  icon: IconName;
  label: string;
  value: string;
  tone: string;
}

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
  protected readonly themeIcon = computed<IconName>(() =>
    ({ auto: 'themeAuto', light: 'sun', dark: 'moon' } as const)[this.theme.mode()],
  );
  protected readonly themeLabel = computed(() => ({ auto: 'automático', light: 'claro', dark: 'escuro' })[this.theme.mode()]);

  /** The most time-sensitive facts, pulled from several widgets into one strip under the clock. */
  protected readonly glance = computed<Glance[]>(() => {
    const now = this.clock.now();
    const items: Glance[] = [];

    const hint = valueOf(this.data.weather)?.hints[0];
    if (hint) {
      items.push({ icon: hint.kind === 'jacket' ? 'shirt' : 'umbrella', label: 'Tempo', value: hint.text, tone: 'sky' });
    }

    const drive = valueOf(this.data.commute);
    if (drive) {
      const diff = drive.currentMinutes - drive.usualMinutes;
      items.push({
        icon: 'car',
        label: drive.label,
        value: `${drive.currentMinutes} min${diff > 0 ? ` (+${diff})` : ''} · chegada ${fmt.time(drive.arriveIfLeavingNow)}`,
        tone: 'teal',
      });
    }

    const client = valueOf(this.data.appointments)?.appointments.find((a) => new Date(a.start) > now);
    if (client) {
      items.push({ icon: 'scissors', label: 'Próximo atendimento', value: `${client.customer} às ${fmt.time(client.start)}`, tone: 'rose' });
    }

    const holiday = valueOf(this.data.holidays)?.holidays[0];
    if (holiday) {
      items.push({ icon: 'flag', label: 'Próximo feriado', value: `${holiday.name} ${inDays(holiday.daysUntil)}`, tone: 'green' });
    }

    return items;
  });
}
