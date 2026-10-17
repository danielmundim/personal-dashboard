import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { Clock, fmt, relativeDay } from '../core/clock';
import { DashboardData } from '../core/dashboard-data';
import { Icon } from '../shared/icon';
import { Tile } from '../shared/tile';
import { tileState, valueOf } from '../shared/resource-state';

/** What's on in the city. Laid out as a row of cards, so it suits a full-width slot. */
@Component({
  selector: 'app-city-events-tile',
  imports: [Tile, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-tile [title]="title()"icon="ticket" accent="amber" [state]="state()" [meta]="list()?.source">
      <ol>
        @for (e of events(); track e.title + e.start) {
          <li [style.--hue]="e.hue">
            <span class="cat">{{ e.category }}</span>
            <strong>{{ e.title }}</strong>
            <span class="where"><app-icon name="mapPin" [size]="13" />{{ e.venue }}</span>
            <span class="foot">
              <span class="when num">{{ e.day }} · {{ fmt.time(e.start) }}</span>
              @if (e.price) {
                <span class="price">{{ e.price }}</span>
              }
            </span>
          </li>
        }
      </ol>
    </app-tile>
  `,
  styles: `
    ol { list-style: none; margin: 0; padding: 0; display: grid; grid-template-columns: repeat(auto-fill, minmax(200px, 1fr)); gap: 12px; }
    li {
      display: flex; flex-direction: column; gap: 4px; padding: 14px; border-radius: 14px;
      background: linear-gradient(160deg, hsl(var(--hue) 60% 50% / 0.16), transparent 70%), var(--surface-2);
      border: 1px solid hsl(var(--hue) 50% 50% / 0.18);
    }
    .cat { font-size: 0.68rem; font-weight: 700; text-transform: uppercase; letter-spacing: 0.06em; color: hsl(var(--hue) 60% var(--hue-text-l)); }
    strong { font-weight: 600; line-height: 1.25; }
    .where { display: flex; align-items: center; gap: 4px; font-size: 0.78rem; color: var(--text-muted); }
    .foot { display: flex; justify-content: space-between; align-items: center; margin-top: auto; padding-top: 6px; font-size: 0.78rem; }
    .when { color: var(--text-muted); font-weight: 600; }
    .price { font-weight: 600; }
  `,
})
export class CityEventsTile {
  private readonly data = inject(DashboardData);
  private readonly clock = inject(Clock);
  protected readonly state = computed(() => tileState(this.data.cityEvents));
  protected readonly list = computed(() => valueOf(this.data.cityEvents));
  protected readonly fmt = fmt;
  protected readonly title = computed(() => (this.list()?.city ? `Eventos em ${this.list()!.city}` : 'Eventos na cidade'));

  private static readonly HUES: Record<string, number> = {
    Música: 265, Feira: 140, Gastronomia: 20, Exposição: 200, Comédia: 45, Clássica: 330,
  };

  protected readonly events = computed(() => {
    const now = this.clock.now();
    return (this.list()?.events ?? []).map((e) => ({
      ...e,
      day: relativeDay(e.start, now),
      hue: CityEventsTile.HUES[e.category] ?? 210,
    }));
  });
}
