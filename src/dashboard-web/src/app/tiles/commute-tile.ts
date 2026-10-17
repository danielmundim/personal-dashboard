import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { Clock, fmt } from '../core/clock';
import { DashboardData } from '../core/dashboard-data';
import { Icon } from '../shared/icon';
import { Tile } from '../shared/tile';
import { tileState, valueOf } from '../shared/resource-state';

const W = 300;
const H = 64;

/** Live drive time (Waze) to a fixed destination, against the usual time for this hour. */
@Component({
  selector: 'app-commute-tile',
  imports: [Tile, Icon, DecimalPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-tile [title]="e()?.label ?? 'Trajeto'" icon="car" accent="teal" [state]="state()" [meta]="source()">
      @if (e(); as e) {
        <div class="big">
          <strong class="num">{{ e.currentMinutes }}</strong><span>min</span>
          <span class="chip" [attr.data-tone]="tone()">{{ trafficLabel() }}</span>
        </div>
        <p class="delta" [class.worse]="diff() > 0">
          @if (diff() > 0) { {{ diff() }} min a mais que {{ baseline() }} } @else if (diff() < 0) { {{ -diff() }} min a menos que {{ baseline() }} } @else { Igual {{ baseline() === 'o normal' ? 'ao normal' : 'a sem trânsito' }} }
        </p>
        <small class="checked muted">Consultado às {{ fmt.time(e.updatedAt) }}</small>
        <dl>
          <div><dt>Chegada</dt><dd class="num">{{ arrival() }}</dd></div>
          <div><dt>Distância</dt><dd class="num">{{ e.distanceKm | number: '1.1-1' }} km</dd></div>
          <div class="via"><dt>Via</dt><dd>{{ e.via }}</dd></div>
        </dl>
        @if (e.incident) {
          <p class="incident"><app-icon name="alert" [size]="14" />{{ e.incident }}</p>
        }
        @if (curve(); as c) {
          <svg [attr.viewBox]="'0 0 ' + w + ' ' + (h + 16)" role="img" aria-label="Tempo típico de trajeto hoje">
            <path class="area" [attr.d]="c.area" />
            <path class="line" [attr.d]="c.line" />
            <line class="now" [attr.x1]="c.nowX" [attr.x2]="c.nowX" y1="0" [attr.y2]="h" />
            <circle class="now-dot" [attr.cx]="c.nowX" [attr.cy]="c.nowY" r="4" />
            @for (t of c.ticks; track t.x) {
              <text [attr.x]="t.x" [attr.y]="h + 13">{{ t.label }}</text>
            }
          </svg>
          <small class="muted caption">Tempo típico (histórico) · ponto = agora</small>
        } @else {
          <small class="muted caption">A curva do dia aparece depois de alguns dias de leituras.</small>
        }
      }
    </app-tile>
  `,
  styles: `
    .big { display: flex; align-items: baseline; gap: 6px; }
    .big strong { font-size: 3rem; font-weight: 600; line-height: 1; letter-spacing: -0.03em; }
    .big > span:not(.chip) { color: var(--text-faint); font-weight: 500; }
    .big .chip { margin-left: auto; align-self: center; }
    .delta { margin: -8px 0 0; font-size: 0.85rem; color: var(--green); font-weight: 500; }
    .delta.worse { color: var(--amber); }
    .checked { margin-top: -10px; font-size: 0.75rem; }
    dl { display: grid; grid-template-columns: auto auto 1fr; gap: 8px; margin: 0; }
    dl div { display: flex; flex-direction: column; padding: 6px 10px; border-radius: 10px; background: var(--surface-2); min-width: 0; }
    dt { font-size: 0.68rem; color: var(--text-faint); }
    dd { margin: 0; font-weight: 600; font-size: 0.88rem; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .incident { display: flex; align-items: center; gap: 6px; margin: 0; font-size: 0.8rem; color: var(--amber); }
    svg { width: 100%; height: auto; overflow: visible; }
    .area { fill: color-mix(in srgb, var(--teal) 14%, transparent); }
    .line { fill: none; stroke: var(--teal); stroke-width: 2; vector-effect: non-scaling-stroke; }
    .now { stroke: var(--text-faint); stroke-dasharray: 3 3; vector-effect: non-scaling-stroke; }
    .now-dot { fill: var(--teal); stroke: var(--surface); stroke-width: 2; }
    text { font-size: 10px; fill: var(--text-faint); text-anchor: middle; }
    .caption { font-size: 0.72rem; margin-top: -8px; }
  `,
})
export class CommuteTile {
  private readonly data = inject(DashboardData);
  private readonly clock = inject(Clock);
  protected readonly state = computed(() => tileState(this.data.commute));
  protected readonly e = computed(() => valueOf(this.data.commute));
  protected readonly fmt = fmt;
  protected readonly w = W;
  protected readonly source = computed(() =>
    (valueOf(this.data.config)?.mockWidgets ?? []).includes('commute') ? 'Exemplo' : 'Google Maps',
  );
  protected readonly h = H;

  /** Leaving now: the reading can be up to 10 min old, so add its duration to the current time, not the request time. */
  protected readonly arrival = computed(() => {
    const e = this.e();
    return e ? fmt.time(new Date(this.clock.now().getTime() + e.currentMinutes * 60_000)) : '';
  });

  protected readonly baseline = computed(() => (this.e()?.usualSource === 'freeFlow' ? 'sem trânsito' : 'o normal'));

  protected readonly diff = computed(() => {
    const e = this.e();
    return e ? e.currentMinutes - e.usualMinutes : 0;
  });

  protected readonly trafficLabel = computed(
    () => ({ light: 'Trânsito leve', moderate: 'Trânsito moderado', heavy: 'Trânsito intenso' })[this.e()?.traffic ?? 'light'],
  );

  protected readonly tone = computed(() => ({ light: 'green', moderate: 'amber', heavy: 'rose' })[this.e()?.traffic ?? 'light']);

  protected readonly curve = computed(() => {
    const pts = this.e()?.today ?? [];
    if (pts.length < 2) return null;
    const first = new Date(pts[0].hour).getTime();
    const last = new Date(pts[pts.length - 1].hour).getTime();
    const max = Math.max(...pts.map((p) => p.minutes)) * 1.1;
    const x = (t: number) => ((t - first) / (last - first)) * W;
    const y = (m: number) => H - (m / max) * H;
    const coords = pts.map((p) => [x(new Date(p.hour).getTime()), y(p.minutes)] as const);
    const line = coords.map(([px, py], i) => `${i ? 'L' : 'M'}${px.toFixed(1)},${py.toFixed(1)}`).join(' ');

    const now = Math.min(Math.max(this.clock.now().getTime(), first), last);
    const current = this.e()!.currentMinutes;
    return {
      line,
      area: `${line} L${W},${H} L0,${H} Z`,
      nowX: x(now),
      nowY: y(current),
      ticks: pts.filter((_, i) => i % 4 === 0).map((p) => ({ x: x(new Date(p.hour).getTime()), label: fmt.hour(p.hour) + 'h' })),
    };
  });
}
