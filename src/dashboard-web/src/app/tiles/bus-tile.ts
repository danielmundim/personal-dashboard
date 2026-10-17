import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { SECOND } from '../core/dashboard-data';
import { DashboardConfig, StopBoard } from '../core/models';
import { pollingResource } from '../core/polling-resource';
import { Icon } from '../shared/icon';
import { Tile } from '../shared/tile';
import { tileState, valueOf } from '../shared/resource-state';

/** Next departures at one bus stop. Rendered once per configured stop. */
@Component({
  selector: 'app-bus-tile',
  imports: [Tile, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-tile [title]="stop().name" icon="bus" accent="amber" [state]="state()">
      <div class="walk muted">
        <app-icon name="walk" [size]="14" />{{ stop().walkMinutes }} min a pé
      </div>
      <ol>
        @for (d of departures(); track $index) {
          <li [class.missed]="d.missed">
            <span class="line">{{ d.line }}</span>
            <span class="dest">
              <strong>{{ d.destination }}</strong>
              <small>
                @if (d.realTime) { <span class="live">●</span> Ao vivo } @else { Programado }
                @if (d.delayed) { · <em>atrasado</em> }
                @if (d.missed) { · não dá tempo }
              </small>
            </span>
            <span class="mins num" [class.soon]="d.leaveNow">
              @if (d.minutesAway === 0) { Agora } @else { {{ d.minutesAway }}<small>min</small> }
            </span>
          </li>
        } @empty {
          <li class="muted">Nenhum ônibus nos próximos 45 min</li>
        }
      </ol>
    </app-tile>
  `,
  styles: `
    .walk { display: flex; align-items: center; gap: 6px; margin-top: -6px; font-size: 0.78rem; }
    ol { list-style: none; margin: 0; padding: 0; display: grid; }
    li { display: grid; grid-template-columns: 46px 1fr auto; align-items: center; gap: 12px; padding: 7px 0; }
    li + li { border-top: 1px solid var(--border); }
    li.missed { opacity: 0.45; }
    .line {
      display: grid; place-items: center; height: 30px; border-radius: 8px;
      font-weight: 700; font-size: 0.85rem; color: #fff; background: #1f6fb2;
    }
    .dest { display: flex; flex-direction: column; min-width: 0; }
    .dest strong { font-weight: 500; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .dest small { font-size: 0.74rem; color: var(--text-faint); }
    .dest em { color: var(--amber); font-style: normal; font-weight: 600; }
    .live { color: var(--green); font-size: 0.6rem; vertical-align: middle; }
    .mins { font-size: 1.45rem; font-weight: 600; text-align: right; }
    .mins small { font-size: 0.7rem; margin-left: 2px; color: var(--text-faint); font-weight: 500; }
    .mins.soon { color: var(--amber); }
  `,
})
export class BusTile {
  readonly stop = input.required<DashboardConfig['busStops'][number]>();

  private readonly res = pollingResource<StopBoard>(() => `/api/transit/${encodeURIComponent(this.stop().id)}`, 30 * SECOND);
  protected readonly state = computed(() => tileState(this.res));

  /** Flags buses you can't reach given the walk, and the ones you should leave for right now. */
  protected readonly departures = computed(() => {
    const walk = this.stop().walkMinutes;
    return (valueOf(this.res)?.departures ?? []).map((d) => ({
      ...d,
      missed: d.minutesAway < walk,
      leaveNow: d.minutesAway >= walk && d.minutesAway <= walk + 2,
    }));
  });
}
