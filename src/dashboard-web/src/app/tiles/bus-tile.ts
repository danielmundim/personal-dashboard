import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { Clock, fmt } from '../core/clock';
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
    <app-tile [title]="stop().name" icon="bus" accent="amber" [state]="state()" [meta]="stop().code">
      <div class="walk muted">
        @if (lines().length) {
          <span>{{ lines().length === 1 ? 'Linha' : 'Linhas' }} {{ lines().join(' · ') }}</span>
          <span aria-hidden="true">·</span>
        }
        <app-icon name="walk" [size]="14" />{{ stop().walkMinutes }} min a pé
      </div>
      <ol>
        @for (d of departures(); track d.key) {
          <li [class.missed]="d.missed">
            <span class="line" [style.background]="d.color" [style.color]="d.textColor">{{ d.line }}</span>
            <span class="dest">
              <strong>{{ d.destination }}</strong>
              <small class="times num">
                @if (d.realTime) { <span class="live" title="Previsão em tempo real">●</span> } @else { <span title="Horário programado">◷</span> }
                @if (d.scheduled && d.delayMinutes !== 0) {
                  <span class="sched">{{ d.scheduled }}</span> → {{ d.arrives }}
                  @if (d.delayMinutes > 0) {
                    <em class="late">+{{ d.delayMinutes }} min</em>
                  } @else {
                    <em class="early">{{ -d.delayMinutes }} min adiantado</em>
                  }
                } @else if (d.arrives) {
                  {{ d.arrives }} · no horário
                }
                @if (d.missed) { · não dá tempo }
              </small>
            </span>
            <span class="mins num" [class.soon]="d.leaveNow">
              @if (d.minutes <= 0) { Agora } @else { {{ d.minutes }}<small>min</small> }
            </span>
          </li>
        } @empty {
          <li class="muted">Nenhum ônibus previsto agora</li>
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
    .times { display: flex; align-items: center; flex-wrap: wrap; gap: 0 5px; }
    .sched { text-decoration: line-through; }
    .dest em { font-style: normal; font-weight: 600; }
    .late { color: var(--amber); }
    .early { color: var(--green); }
    .live { color: var(--green); font-size: 0.6rem; }
    .mins { font-size: 1.45rem; font-weight: 600; text-align: right; }
    .mins small { font-size: 0.7rem; margin-left: 2px; color: var(--text-faint); font-weight: 500; }
    .mins.soon { color: var(--amber); }
  `,
})
export class BusTile {
  readonly stop = input.required<DashboardConfig['busStops'][number]>();

  private readonly clock = inject(Clock);
  private readonly res = pollingResource<StopBoard>(() => `/api/transit/${encodeURIComponent(this.stop().id)}`, 30 * SECOND);
  protected readonly state = computed(() => tileState(this.res));
  protected readonly lines = computed(() => valueOf(this.res)?.lines ?? []);

  /**
   * Counts down from the arrival timestamp on every clock tick, so the minutes stay exact between
   * polls. Buses that have left drop off; the ones you can't reach on foot are dimmed.
   */
  protected readonly departures = computed(() => {
    const walk = this.stop().walkMinutes;
    const now = this.clock.now().getTime();
    return (valueOf(this.res)?.departures ?? [])
      .map((d, i) => {
        const at = d.arrivesAt ? new Date(d.arrivesAt).getTime() : null;
        const minutes = at === null ? d.minutesAway : Math.floor((at - now) / 60_000);
        return {
          ...d,
          key: `${d.line}|${d.scheduledAt ?? d.arrivesAt ?? i}`,
          gone: at !== null && at < now - 30_000,
          minutes,
          arrives: d.arrivesAt ? fmt.time(d.arrivesAt) : null,
          scheduled: d.scheduledAt ? fmt.time(d.scheduledAt) : null,
          missed: minutes < walk,
          leaveNow: minutes >= walk && minutes <= walk + 2,
        };
      })
      .filter((d) => !d.gone);
  });
}
