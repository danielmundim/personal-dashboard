import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { Clock, daysUntil, fmt } from '../core/clock';
import { DashboardData } from '../core/dashboard-data';
import { Icon } from '../shared/icon';
import { Tile } from '../shared/tile';
import { tileState, valueOf } from '../shared/resource-state';

/** Parties we confirmed attending. */
@Component({
  selector: 'app-parties-tile',
  imports: [Tile, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-tile title="Festas" icon="party" accent="violet" [state]="state()" meta="Confirmadas">
      <ol>
        @for (p of parties(); track p.title + p.start) {
          <li>
            <span class="date">
              <strong class="num">{{ fmt.dayNum(p.start) }}</strong>
              <small>{{ fmt.monthShort(p.start) }}</small>
            </span>
            <span class="what">
              <strong>{{ p.title }}</strong>
              <small>{{ fmt.weekday(p.start) }} {{ fmt.time(p.start) }} · {{ p.location }}</small>
              @if (p.bring || p.dressCode) {
                <span class="extras">
                  @if (p.bring) { <span class="chip" data-tone="amber"><app-icon name="gift" [size]="12" />{{ p.bring }}</span> }
                  @if (p.dressCode) { <span class="chip" data-tone="violet"><app-icon name="shirt" [size]="12" />{{ p.dressCode }}</span> }
                </span>
              }
            </span>
            <span class="in num">{{ p.days === 0 ? 'Hoje' : p.days === 1 ? 'Amanhã' : 'em ' + p.days + ' d' }}</span>
          </li>
        } @empty {
          <li class="muted">Nenhuma festa confirmada ainda.</li>
        }
      </ol>
    </app-tile>
  `,
  styles: `
    ol { list-style: none; margin: 0; padding: 0; display: grid; }
    li { display: grid; grid-template-columns: 42px 1fr auto; align-items: start; gap: 12px; padding: 8px 0; }
    li + li { border-top: 1px solid var(--border); }
    .date { display: flex; flex-direction: column; align-items: center; justify-content: center; height: 42px; border-radius: 10px; line-height: 1.05;
      color: var(--violet); background: color-mix(in srgb, var(--violet) 14%, var(--surface-2)); }
    .date strong { font-size: 1.05rem; }
    .date small { font-size: 0.62rem; text-transform: uppercase; }
    .what { display: flex; flex-direction: column; gap: 1px; min-width: 0; }
    .what strong { font-weight: 500; }
    .what small { font-size: 0.76rem; color: var(--text-faint); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .extras { display: flex; flex-wrap: wrap; gap: 4px; margin-top: 4px; }
    .in { font-size: 0.8rem; color: var(--text-muted); white-space: nowrap; padding-top: 2px; }
  `,
})
export class PartiesTile {
  private readonly data = inject(DashboardData);
  private readonly clock = inject(Clock);
  protected readonly state = computed(() => tileState(this.data.parties));
  protected readonly fmt = fmt;

  protected readonly parties = computed(() =>
    (valueOf(this.data.parties)?.parties ?? []).map((p) => ({ ...p, days: daysUntil(fmt.dayKey(p.start), this.clock.now()) })),
  );
}
