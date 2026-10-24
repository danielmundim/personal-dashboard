import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Clock, daysUntil, fmt } from '../core/clock';
import { Trips, progress } from '../core/trips';
import { Icon } from '../shared/icon';
import { Tile } from '../shared/tile';
import { tileState } from '../shared/resource-state';

/** Hoje: the next trip, how far away it is, and what's still open on its checklist. */
@Component({
  selector: 'app-trip-summary-tile',
  imports: [Tile, Icon, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-tile title="Próxima viagem" icon="plane" accent="teal" [state]="state()">
      @if (trip(); as t) {
        <a class="head" routerLink="/viagens">
          <span class="what">
            <strong>{{ t.name }}</strong>
            <small>{{ t.dates }}@if (t.destination) { · {{ t.destination }} }</small>
          </span>
          <span class="days num">
            @if (t.ongoing) { <b>Agora</b> } @else { <b>{{ t.days }}</b><small>{{ t.days === 1 ? 'dia' : 'dias' }}</small> }
          </span>
        </a>
        @if (t.progress.total) {
          <div class="progress" [attr.aria-label]="t.progress.done + ' de ' + t.progress.total + ' itens prontos'">
            <span [style.width.%]="t.progress.pct"></span>
          </div>
          <small class="muted">{{ t.progress.done }} de {{ t.progress.total }} itens prontos</small>
          @if (t.pending.length) {
            <ul>
              @for (i of t.pending; track i.id) { <li>{{ i.text }}</li> }
            </ul>
          }
        } @else {
          <p class="muted">Nenhum item no checklist ainda.</p>
        }
      } @else {
        <p class="muted">Nenhuma viagem planejada.</p>
      }
      <a class="open" routerLink="/viagens">Abrir viagens <app-icon name="arrowRight" [size]="14" /></a>
    </app-tile>
  `,
  styles: `
    .head { display: grid; grid-template-columns: 1fr auto; gap: 12px; color: inherit; text-decoration: none; }
    .what { display: flex; flex-direction: column; min-width: 0; }
    .what strong { font-size: 1.05rem; font-weight: 600; }
    .what small { color: var(--text-faint); font-size: 0.78rem; }
    .days { display: flex; flex-direction: column; align-items: flex-end; line-height: 1; }
    .days b { font-size: 2.2rem; font-weight: 600; letter-spacing: -0.03em; color: var(--teal); }
    .days small { font-size: 0.72rem; color: var(--text-faint); }
    .progress { height: 6px; border-radius: 3px; background: var(--surface-2); overflow: hidden; }
    .progress span { display: block; height: 100%; border-radius: 3px; background: var(--teal); }
    ul { list-style: none; margin: 0; padding: 0; display: grid; gap: 3px; font-size: 0.86rem; }
    li { padding-left: 14px; position: relative; }
    li::before { content: ''; position: absolute; left: 0; top: 0.45em; width: 7px; height: 7px; border: 1.5px solid var(--text-faint); border-radius: 2px; }
    p { margin: 0; }
    .open { display: inline-flex; align-items: center; gap: 4px; margin-top: auto; align-self: flex-end; font-size: 0.82rem; font-weight: 600; color: var(--teal); text-decoration: none; }
  `,
})
export class TripSummaryTile {
  private readonly trips = inject(Trips);
  private readonly clock = inject(Clock);
  protected readonly state = computed(() => tileState(this.trips.res));

  protected readonly trip = computed(() => {
    const t = this.trips.next();
    if (!t) return null;
    const now = this.clock.now();
    const days = daysUntil(t.startDate, now);
    return {
      ...t,
      days,
      ongoing: days <= 0,
      dates: t.endDate ? `${fmt.dateOnlyShort(t.startDate)} – ${fmt.dateOnlyShort(t.endDate)}` : fmt.dateOnlyShort(t.startDate),
      progress: progress(t),
      pending: t.checklist.filter((i) => !i.done).slice(0, 3),
    };
  });
}
