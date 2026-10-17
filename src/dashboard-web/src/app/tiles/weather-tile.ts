import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { DashboardData } from '../core/dashboard-data';
import { fmt } from '../core/clock';
import { HourlyForecast, WeatherReport } from '../core/models';
import { Icon, IconName, weatherIcon } from '../shared/icon';
import { Tile } from '../shared/tile';
import { tileState, valueOf } from '../shared/resource-state';

const CHART_W = 640;
const CHART_H = 118;
const DETAIL_HOURS = 6;

const HINT_ICONS: Record<WeatherReport['hints'][number]['kind'], IconName> = {
  umbrella: 'umbrella',
  jacket: 'shirt',
  sunscreen: 'sun',
  wind: 'wind',
};

/** Stable key for styling (data-uv attribute); the label is what the screen shows. */
export function uvLevel(uv: number): 'low' | 'moderate' | 'high' {
  return uv <= 2 ? 'low' : uv <= 5 ? 'moderate' : 'high';
}

export function uvLabel(uv: number) {
  return uv <= 2 ? 'Baixo' : uv <= 5 ? 'Moderado' : uv <= 7 ? 'Alto' : uv <= 10 ? 'Muito alto' : 'Extremo';
}

@Component({
  selector: 'app-weather-tile',
  imports: [Tile, Icon, DecimalPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './weather-tile.html',
  styleUrl: './weather-tile.scss',
})
export class WeatherTile {
  private readonly data = inject(DashboardData);
  protected readonly res = this.data.weather;
  protected readonly state = computed(() => tileState(this.res));
  protected readonly w = computed(() => valueOf(this.res));
  protected readonly fmt = fmt;
  protected readonly weatherIcon = weatherIcon;
  protected readonly uvLevel = uvLevel;
  protected readonly uvLabel = uvLabel;
  protected readonly hintIcons = HINT_ICONS;
  protected readonly chartW = CHART_W;
  protected readonly chartH = CHART_H;

  /** Detailed columns for the next few hours: rain, wind and UV per hour. */
  protected readonly nextHours = computed(() => (this.w()?.hourly ?? []).slice(0, DETAIL_HOURS));

  /** The rest of today (at least 8 hours so the evening still shows a useful curve). */
  private readonly restOfDay = computed<HourlyForecast[]>(() => {
    const hours = this.w()?.hourly ?? [];
    if (!hours.length) return [];
    const today = fmt.dayKey(hours[0].time);
    const sameDay = hours.filter((h) => fmt.dayKey(h.time) === today);
    return sameDay.length >= 8 ? sameDay : hours.slice(0, 8);
  });

  protected readonly chart = computed(() => {
    const hours = this.restOfDay();
    if (!hours.length) return null;
    const temps = hours.map((h) => h.temperatureC);
    const min = Math.min(...temps) - 1;
    const max = Math.max(...temps) + 1;
    const step = CHART_W / hours.length;
    const y = (t: number) => 20 + (1 - (t - min) / (max - min)) * 40;

    const points = hours.map((h, i) => ({
      x: step * i + step / 2,
      y: y(h.temperatureC),
      temp: Math.round(h.temperatureC),
      label: i === 0 ? 'Agora' : fmt.hour(h.time),
      rainH: (h.precipitationPct / 100) * 30,
      rain: h.precipitationPct,
    }));
    return {
      points,
      barW: Math.min(22, step * 0.55),
      line: points.map((p, i) => `${i ? 'L' : 'M'}${p.x.toFixed(1)},${p.y.toFixed(1)}`).join(' '),
      title:
        fmt.dayKey(hours[hours.length - 1].time) === fmt.dayKey(hours[0].time)
          ? `Hoje, agora – ${fmt.hour(hours[hours.length - 1].time)}h`
          : `Agora – ${fmt.hour(hours[hours.length - 1].time)}h de amanhã`,
    };
  });

  /** 7-day rows with a temperature range bar positioned on the week's overall min..max scale. */
  protected readonly week = computed(() => {
    const days = this.w()?.daily ?? [];
    if (!days.length) return [];
    const lo = Math.min(...days.map((d) => d.minC));
    const hi = Math.max(...days.map((d) => d.maxC));
    const span = hi - lo || 1;
    return days.map((d, i) => ({
      ...d,
      label: i === 0 ? 'Hoje' : i === 1 ? 'Amanhã' : fmt.dateOnlyShortWeekday(d.date),
      left: ((d.minC - lo) / span) * 100,
      width: ((d.maxC - d.minC) / span) * 100,
    }));
  });

  protected readonly daylight = computed(() => {
    const sun = this.w()?.sun;
    if (!sun) return null;
    const [h, m] = sun.daylightLength.split(':').map(Number);
    const [cm, cs] = sun.daylightChangeVsYesterday.replace(/^-?00:/, '').split('.')[0].split(':').map(Number);
    const sign = sun.daylightChangeVsYesterday.startsWith('-') ? '−' : '+';
    return { length: `${h}h ${m}m`, change: `${sign}${cm}m ${cs}s` };
  });
}
