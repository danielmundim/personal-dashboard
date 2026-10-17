import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { Clock, fmt, relativeDay } from '../core/clock';
import { MINUTE } from '../core/dashboard-data';
import { CalendarAgenda, DashboardConfig } from '../core/models';
import { pollingResource } from '../core/polling-resource';
import { Tile } from '../shared/tile';
import { tileState, valueOf } from '../shared/resource-state';

const MAX_EVENTS = 7;

/** One person's upcoming events, grouped by day. Rendered once per configured person. */
@Component({
  selector: 'app-calendar-tile',
  imports: [Tile],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-tile [title]="person().name" icon="calendar" [accent]="person().color" [state]="state()" meta="Próximos 7 dias">
      @if (agenda()?.failedSources?.length) {
        <p class="warn">Não foi possível ler: {{ agenda()!.failedSources!.join(', ') }}</p>
      }
      @if (agenda() && !agenda()!.configured) {
        <p class="muted setup">Adicione o link iCal do calendário em <code>appsettings.Local.json</code> para ver os eventos aqui.</p>
      } @else {
      @for (day of days(); track day.key) {
        <section class="day">
          <h3>{{ day.label }}</h3>
          <ol>
            @for (e of day.events; track e.title + e.start) {
              <li [class.past]="e.past" [class.current]="e.current" [class.all-day]="e.allDay" [class.busy]="e.busy">
                <span class="time num">{{ e.allDay ? 'Dia todo' : fmt.time(e.start) }}</span>
                <span class="bar"></span>
                <span class="what">
                  <strong>{{ e.title }}</strong>
                  @if (e.details) {
                    <small>{{ e.details }}</small>
                  }
                </span>
                @if (e.current) {
                  <span class="chip" [attr.data-tone]="person().color">Agora</span>
                }
              </li>
            }
          </ol>
        </section>
      } @empty {
        <p class="muted">Nada nos próximos 7 dias.</p>
      }
      }
    </app-tile>
  `,
  styleUrl: './calendar-tile.scss',
})
export class CalendarTile {
  readonly person = input.required<DashboardConfig['people'][number]>();

  private readonly clock = inject(Clock);
  private readonly res = pollingResource<CalendarAgenda>(() => `/api/calendar/${encodeURIComponent(this.person().id)}`, 5 * MINUTE);
  protected readonly state = computed(() => tileState(this.res));
  protected readonly agenda = computed(() => valueOf(this.res));
  protected readonly fmt = fmt;

  protected readonly days = computed(() => {
    const now = this.clock.now();
    const upcoming = (valueOf(this.res)?.events ?? [])
      .filter((e) => new Date(e.end) > now || fmt.dayKey(e.start) === fmt.dayKey(now))
      .sort((a, b) => Number(b.allDay) - Number(a.allDay) || a.start.localeCompare(b.start))
      .sort((a, b) => fmt.dayKey(a.start).localeCompare(fmt.dayKey(b.start)))
      .slice(0, MAX_EVENTS);

    const multipleSources = new Set((valueOf(this.res)?.events ?? []).map((e) => e.source)).size > 1;
    const groups = new Map<string, { key: string; label: string; events: (typeof upcoming[number] & { past: boolean; current: boolean; details: string })[] }>();
    for (const e of upcoming) {
      const key = fmt.dayKey(e.start);
      if (!groups.has(key)) groups.set(key, { key, label: relativeDay(e.start, now), events: [] });
      groups.get(key)!.events.push({
        ...e,
        // Feed name only matters when more than one calendar feeds this tile.
        details: [
          e.location,
          e.allDay ? null : 'até ' + fmt.time(e.end),
          multipleSources ? e.source : null,
        ].filter(Boolean).join(' · '),
        past: !e.allDay && new Date(e.end) < now,
        current: !e.allDay && new Date(e.start) <= now && now < new Date(e.end),
      });
    }
    return [...groups.values()];
  });
}
