import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { Router } from '@angular/router';
import { Stock } from '../core/stock';
import { Icon } from '../shared/icon';
import { ShoppingTile } from '../tiles/shopping-tile';
import { StockPanel } from '../tiles/stock-panel';

/**
 * Compras, in two views:
 * - Lista de compras: the items we usually buy (tap what ran out) and the next supermarket list.
 * - Estoque: everything we have at home, with quantities, where it's kept and expiry dates.
 * The view is in the URL (?ver=estoque) so a tablet can be left on either.
 */
@Component({
  selector: 'app-shopping-page',
  imports: [ShoppingTile, StockPanel, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="views" role="tablist" aria-label="Compras">
      <button type="button" role="tab" [class.on]="view() === 'lista'" [attr.aria-selected]="view() === 'lista'" (click)="show('lista')">
        <app-icon name="cart" [size]="16" />Lista de compras
      </button>
      <button type="button" role="tab" [class.on]="view() === 'estoque'" [attr.aria-selected]="view() === 'estoque'" (click)="show('estoque')">
        <app-icon name="package" [size]="16" />Estoque
        @if (alerts()) { <span class="count">{{ alerts() }}</span> }
      </button>
    </div>

    @if (view() === 'estoque') {
      <app-stock-panel />
    } @else {
      <app-shopping-tile class="wide" />
    }
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: 16px; }
    .views { display: inline-flex; align-self: flex-start; gap: 4px; padding: 4px; border: 1px solid var(--border); border-radius: 14px; background: var(--surface); }
    .views button {
      display: inline-flex; align-items: center; gap: 8px; height: 40px; padding: 0 16px; border: 0; border-radius: 10px;
      font: inherit; font-size: 0.9rem; font-weight: 600; color: var(--text-muted); background: transparent; cursor: pointer;
    }
    .views button.on { color: #fff; background: var(--green); }
    .count { padding: 1px 7px; border-radius: 999px; font-size: 0.72rem; color: #fff; background: var(--amber); }
    app-shopping-tile { display: flex; min-width: 0; }
    app-shopping-tile ::ng-deep > app-tile { flex: 1; }
  `,
})
export class ShoppingPage {
  /** ?ver=estoque */
  readonly ver = input<string>();

  private readonly router = inject(Router);
  private readonly stock = inject(Stock);
  protected readonly view = computed(() => (this.ver() === 'estoque' ? 'estoque' : 'lista'));
  protected readonly alerts = this.stock.attentionCount;

  protected show(view: 'lista' | 'estoque') {
    this.router.navigate([], { queryParams: { ver: view === 'estoque' ? 'estoque' : null }, replaceUrl: true });
  }
}
