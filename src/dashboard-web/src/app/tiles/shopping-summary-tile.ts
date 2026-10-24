import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { SECOND } from '../core/dashboard-data';
import { ShoppingList } from '../core/models';
import { pollingResource } from '../core/polling-resource';
import { Stock } from '../core/stock';
import { openShoppingListInWhatsApp } from '../core/whatsapp';
import { Icon } from '../shared/icon';
import { Tile } from '../shared/tile';
import { tileState, valueOf } from '../shared/resource-state';

const SHOWN = 5;

/** Hoje: how many things are missing, the first few, and a shortcut to send or open the full list. */
@Component({
  selector: 'app-shopping-summary-tile',
  imports: [Tile, Icon, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-tile title="Compras" icon="cart" accent="green" [state]="state()">
      <a class="count" routerLink="/compras">
        <strong class="num">{{ needed().length }}</strong>
        <span>{{ needed().length === 1 ? 'item em falta' : 'itens em falta' }}</span>
      </a>
      @if (needed().length) {
        <ul>
          @for (i of needed().slice(0, shown); track i.id) {
            <li>{{ i.quantity > 1 ? i.quantity + '× ' : '' }}{{ i.name }}</li>
          }
          @if (needed().length > shown) {
            <li class="more muted">+{{ needed().length - shown }} mais</li>
          }
        </ul>
      } @else {
        <p class="muted">Nada em falta por agora.</p>
      }
      @if (stockAlerts()) {
        <a class="stock-alert" routerLink="/compras" [queryParams]="{ ver: 'estoque' }">
          <app-icon name="alert" [size]="14" />{{ stockAlerts() }} {{ stockAlerts() === 1 ? 'item do estoque precisa' : 'itens do estoque precisam' }} de atenção
        </a>
      }
      <div class="actions">
        @if (needed().length) {
          <button type="button" class="whatsapp" (click)="send()" [disabled]="sending()">
            <app-icon name="send" [size]="15" />WhatsApp
          </button>
        }
        <a class="open" routerLink="/compras">Abrir compras <app-icon name="arrowRight" [size]="14" /></a>
      </div>
      @if (error()) {
        <p class="error">{{ error() }}</p>
      }
    </app-tile>
  `,
  styles: `
    .count { display: flex; align-items: baseline; gap: 8px; color: inherit; text-decoration: none; }
    .count strong { font-size: 2.6rem; font-weight: 600; line-height: 1; letter-spacing: -0.03em; }
    .count span { color: var(--text-muted); }
    ul { list-style: none; margin: 0; padding: 0; display: grid; gap: 4px; }
    li { padding-left: 14px; position: relative; font-weight: 500; }
    li::before { content: ''; position: absolute; left: 0; top: 0.6em; width: 6px; height: 6px; border-radius: 50%; background: var(--green); }
    li.more { padding-left: 0; font-weight: 400; font-size: 0.85rem; }
    li.more::before { display: none; }
    p { margin: 0; }
    .actions { display: flex; flex-wrap: wrap; align-items: center; gap: 10px; margin-top: auto; }
    .whatsapp {
      display: inline-flex; align-items: center; gap: 6px; height: 34px; padding: 0 12px; border: 0; border-radius: 9px;
      font: inherit; font-size: 0.82rem; font-weight: 600; color: #fff; background: #1fa855; cursor: pointer;
    }
    .whatsapp:disabled { opacity: 0.5; }
    .open { display: inline-flex; align-items: center; gap: 4px; margin-left: auto; font-size: 0.82rem; font-weight: 600; color: var(--green); text-decoration: none; }
    .error { font-size: 0.8rem; color: var(--rose); }
    .stock-alert { display: inline-flex; align-items: center; gap: 6px; font-size: 0.82rem; font-weight: 600; color: var(--amber); text-decoration: none; }
  `,
})
export class ShoppingSummaryTile {
  private readonly http = inject(HttpClient);
  private readonly res = pollingResource<ShoppingList>('/api/shopping', 20 * SECOND);
  protected readonly state = computed(() => tileState(this.res));
  protected readonly shown = SHOWN;
  protected readonly sending = signal(false);
  protected readonly error = signal<string | null>(null);
  private readonly stock = inject(Stock);
  protected readonly stockAlerts = this.stock.attentionCount;

  /** Same order as the Compras tab: what ran out first comes first. */
  protected readonly needed = computed(() =>
    (valueOf(this.res)?.items ?? [])
      .filter((i) => i.needed)
      .sort((a, b) => (a.neededSince ?? '').localeCompare(b.neededSince ?? '')),
  );

  protected async send() {
    this.sending.set(true);
    const ok = await openShoppingListInWhatsApp(this.http);
    this.sending.set(false);
    this.error.set(ok ? null : 'Não foi possível preparar a mensagem do WhatsApp.');
  }
}
