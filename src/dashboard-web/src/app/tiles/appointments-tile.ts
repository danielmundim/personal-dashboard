import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { Clock, fmt, relativeDay, startsIn } from '../core/clock';
import { DashboardData } from '../core/dashboard-data';
import { Icon } from '../shared/icon';
import { Tile } from '../shared/tile';
import { tileState, valueOf } from '../shared/resource-state';

/** Upcoming salon clients from Noona. */
@Component({
  selector: 'app-appointments-tile',
  imports: [Tile, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-tile title="Próximos clientes" icon="scissors" accent="rose" [state]="state()" meta="Noona">
      @if (list(); as l) {
        <div class="summary">
          <strong class="num">{{ l.todayRemaining }}</strong>
          <span>de {{ l.todayCount }} clientes restantes hoje</span>
        </div>
        <ol>
          @for (a of items(); track a.customer + a.start) {
            <li [attr.data-status]="a.status">
              <span class="when">
                <strong class="num">{{ fmt.time(a.start) }}</strong>
                <small>{{ a.dayLabel }}</small>
              </span>
              <span class="who">
                <strong>
                  {{ a.customer }}
                  @if (a.firstVisit) {
                    <span class="chip" data-tone="green">1ª vez</span>
                  }
                </strong>
                <small>{{ a.service }} · {{ a.durationMinutes }} min</small>
                @if (a.note) {
                  <small class="note"><app-icon name="alert" [size]="12" />{{ a.note }}</small>
                }
              </span>
              <span class="status">
                @switch (a.status) {
                  @case ('checkedIn') { <span class="chip" data-tone="rose">Atendendo</span> }
                  @case ('unconfirmed') { <span class="chip" data-tone="amber">Não confirmado</span> }
                  @default { <small class="muted">{{ a.startsIn }}</small> }
                }
              </span>
            </li>
          }
        </ol>
      }
    </app-tile>
  `,
  styles: `
    .summary { display: flex; align-items: baseline; gap: 8px; color: var(--text-muted); font-size: 0.88rem; }
    .summary strong { font-size: 1.8rem; font-weight: 600; color: var(--text); }
    ol { list-style: none; margin: 0; padding: 0; display: grid; }
    li { display: grid; grid-template-columns: 52px 1fr auto; align-items: center; gap: 12px; padding: 8px 0; }
    li + li { border-top: 1px solid var(--border); }
    .when { display: flex; flex-direction: column; line-height: 1.15; }
    .when strong { font-weight: 600; }
    .when small { font-size: 0.7rem; color: var(--text-faint); }
    .who { display: flex; flex-direction: column; min-width: 0; }
    .who strong { display: flex; align-items: center; gap: 6px; font-weight: 500; }
    .who small { font-size: 0.78rem; color: var(--text-faint); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .who .note { display: flex; align-items: center; gap: 4px; color: var(--amber); }
    [data-status='checkedIn'] .when strong { color: var(--rose); }
    .status small { white-space: nowrap; }
  `,
})
export class AppointmentsTile {
  private readonly data = inject(DashboardData);
  private readonly clock = inject(Clock);
  protected readonly state = computed(() => tileState(this.data.appointments));
  protected readonly list = computed(() => valueOf(this.data.appointments));
  protected readonly fmt = fmt;

  protected readonly items = computed(() => {
    const now = this.clock.now();
    return (this.list()?.appointments ?? []).map((a) => ({
      ...a,
      dayLabel: relativeDay(a.start, now),
      startsIn: startsIn(a.start, now),
    }));
  });
}
