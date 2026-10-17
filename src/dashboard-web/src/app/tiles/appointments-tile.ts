import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
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
      @if (flash(); as f) {
        <p class="flash" [attr.data-ok]="f.ok">{{ f.text }}</p>
      }
      @if (needsConnect()) {
        <div class="connect">
          <p>Conecte a conta Noona do salão para ver os próximos clientes aqui.</p>
          <a class="connect-btn" href="/api/noona/connect"><app-icon name="scissors" [size]="16" />Conectar ao Noona</a>
          <small class="muted">Faça isso neste computador: depois do login, o Noona volta para o painel.</small>
        </div>
      } @else if (list(); as l) {
        <div class="summary">
          @if (l.todayCount > 0) {
            <strong class="num">{{ l.todayRemaining }}</strong>
            <span>de {{ l.todayCount }} {{ l.todayCount === 1 ? 'cliente restante' : 'clientes restantes' }} hoje</span>
          } @else {
            <span>Nenhum cliente hoje</span>
          }
        </div>
        <ol>
          @for (a of items(); track a.customer + a.start) {
            <li [attr.data-status]="a.status">
              <span class="when">
                <strong class="num">{{ fmt.time(a.start) }}<span class="end">–{{ a.end }}</span></strong>
                <small>{{ a.dayLabel }}</small>
              </span>
              <span class="who">
                <strong>
                  {{ a.customer }}
                  @if (a.firstVisit) {
                    <span class="chip" data-tone="green">1ª vez</span>
                  }
                </strong>
                <small>{{ a.service }} · {{ a.length }}</small>
                @if (a.note) {
                  <small class="note"><app-icon name="alert" [size]="12" />{{ a.note }}</small>
                }
              </span>
              <span class="status">
                @switch (a.status) {
                  @case ('checkedIn') { <span class="chip" data-tone="rose">Atendendo</span> }
                  @case ('unconfirmed') { <span class="chip" data-tone="amber">Não confirmado</span> }
                  @default { @if (a.soon) { <small class="muted">{{ a.startsIn }}</small> } }
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
    li { display: grid; grid-template-columns: auto 1fr auto; align-items: center; gap: 12px; padding: 8px 0; }
    .when strong { white-space: nowrap; }
    .when .end { font-weight: 500; color: var(--text-muted); }
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
    .connect { display: flex; flex-direction: column; align-items: flex-start; gap: 10px; }
    .connect p { margin: 0; color: var(--text-muted); font-size: 0.9rem; }
    .connect small { font-size: 0.75rem; }
    .connect-btn {
      display: inline-flex; align-items: center; gap: 8px; padding: 9px 14px; border-radius: 10px;
      font-weight: 600; color: #fff; background: var(--rose); text-decoration: none;
    }
    .flash { margin: 0; padding: 8px 12px; border-radius: 10px; font-size: 0.82rem; font-weight: 600;
      color: var(--rose); background: color-mix(in srgb, var(--rose) 12%, transparent); }
    .flash[data-ok='true'] { color: var(--green); background: color-mix(in srgb, var(--green) 12%, transparent); }
  `,
})
export class AppointmentsTile {
  private readonly data = inject(DashboardData);
  private readonly clock = inject(Clock);
  /** 409 from the API means "not connected yet": show the connect button rather than an error. */
  protected readonly needsConnect = computed(() => {
    const err = this.data.appointments.error();
    return err instanceof HttpErrorResponse ? err.status === 409 : (err as { cause?: HttpErrorResponse })?.cause?.status === 409;
  });
  protected readonly state = computed(() => (this.needsConnect() ? 'ready' : tileState(this.data.appointments)));

  /** Result of the Noona consent round-trip (/?noona=...), shown once and then removed from the URL. */
  protected readonly flash = signal<{ ok: boolean; text: string } | null>(this.readFlash());
  protected readonly list = computed(() => valueOf(this.data.appointments));
  protected readonly fmt = fmt;

  private readFlash() {
    const result = new URLSearchParams(location.search).get('noona');
    if (!result) return null;
    history.replaceState(null, '', location.pathname);
    setTimeout(() => this.flash.set(null), 10_000);
    const messages: Record<string, { ok: boolean; text: string }> = {
      connected: { ok: true, text: 'Noona conectado! Os clientes aparecem em instantes.' },
      denied: { ok: false, text: 'A conexão com o Noona foi cancelada.' },
      invalid: { ok: false, text: 'O link de conexão expirou. Tente de novo.' },
      failed: { ok: false, text: 'Não foi possível concluir a conexão com o Noona. Veja os logs da API.' },
      'not-configured': { ok: false, text: 'Configure o Client ID e o Client Secret do Noona na API.' },
    };
    if (result === 'connected') setTimeout(() => this.data.appointments.reload(), 500);
    return messages[result] ?? null;
  }

  protected readonly items = computed(() => {
    const now = this.clock.now();
    return (this.list()?.appointments ?? []).map((a) => ({
      ...a,
      dayLabel: relativeDay(a.start, now),
      end: fmt.time(new Date(new Date(a.start).getTime() + a.durationMinutes * 60_000)),
      length: a.durationMinutes >= 60
        ? `${Math.floor(a.durationMinutes / 60)} h${a.durationMinutes % 60 ? ' ' + (a.durationMinutes % 60) + ' min' : ''}`
        : `${a.durationMinutes} min`,
      startsIn: startsIn(a.start, now),
      // Only within 12 h: beyond that startsIn just repeats the day and time already shown on the left.
      soon: new Date(a.start).getTime() - now.getTime() < 12 * 3_600_000,
    }));
  });
}
