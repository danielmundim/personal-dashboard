import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { fmt } from '../core/clock';
import { DashboardData } from '../core/dashboard-data';
import { Tile } from '../shared/tile';
import { tileState, valueOf } from '../shared/resource-state';

/** Next public holidays in Portugal (computed in the API, not a mock). */
@Component({
  selector: 'app-holidays-tile',
  imports: [Tile],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-tile title="Feriados" icon="flag" accent="green" [state]="state()" meta="Portugal">
      <ol>
        @for (h of holidays(); track h.date) {
          <li>
            <span class="date">
              <strong class="num">{{ h.dayNum }}</strong>
              <small>{{ h.month }}</small>
            </span>
            <span class="what">
              <strong>{{ h.name }}</strong>
              <small>{{ h.weekday }}</small>
            </span>
            <span class="side">
              <span class="num in">{{ h.daysUntil === 0 ? 'Hoje' : h.daysUntil === 1 ? 'Amanhã' : 'em ' + h.daysUntil + ' d' }}</span>
              @if (h.longWeekend) {
                <span class="chip" data-tone="green">Feriadão</span>
              } @else if (h.bridgeDay) {
                <span class="chip" data-tone="amber">Dá ponte</span>
              } @else if (h.scope !== 'national') {
                <span class="chip">{{ h.scope === 'municipal' ? 'Municipal' : 'Facultativo' }}</span>
              }
            </span>
          </li>
        }
      </ol>
    </app-tile>
  `,
  styles: `
    ol { list-style: none; margin: 0; padding: 0; display: grid; }
    li { display: grid; grid-template-columns: 42px 1fr auto; align-items: center; gap: 12px; padding: 7px 0; }
    li + li { border-top: 1px solid var(--border); }
    .date { display: flex; flex-direction: column; align-items: center; justify-content: center; height: 42px; border-radius: 10px; background: var(--surface-2); line-height: 1.05; }
    .date strong { font-size: 1.05rem; }
    .date small { font-size: 0.62rem; text-transform: uppercase; color: var(--text-faint); }
    .what { display: flex; flex-direction: column; min-width: 0; }
    .what strong { font-weight: 500; line-height: 1.25; }
    .what small { font-size: 0.76rem; color: var(--text-faint); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .side { display: flex; flex-direction: column; align-items: flex-end; gap: 3px; }
    .in { font-size: 0.8rem; color: var(--text-muted); white-space: nowrap; }
  `,
})
export class HolidaysTile {
  private readonly data = inject(DashboardData);
  protected readonly state = computed(() => tileState(this.data.holidays));

  protected readonly holidays = computed(() =>
    (valueOf(this.data.holidays)?.holidays ?? []).map((h) => ({
      ...h,
      weekday: fmt.dateOnlyWeekday(h.date),
      dayNum: new Date(h.date).getUTCDate(),
      month: fmt.dateOnlyMonthShort(h.date),
    })),
  );
}
