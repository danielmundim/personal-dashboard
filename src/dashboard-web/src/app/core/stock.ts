import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { SECOND } from './dashboard-data';
import { SaveStockItem, StockList, StockView } from './models';
import { pollingResource } from './polling-resource';
import { valueOf } from '../shared/resource-state';
import { saveErrorMessage } from './save-error';

/**
 * The household inventory, shared by the Estoque view and the shopping card on Hoje. Every change
 * returns the whole list from the API, which replaces the local copy.
 */
@Injectable({ providedIn: 'root' })
export class Stock {
  private readonly http = inject(HttpClient);
  readonly res = pollingResource<StockList>('/api/stock', 30 * SECOND);

  readonly items = computed(() => valueOf(this.res)?.items ?? []);
  readonly locations = computed(() => valueOf(this.res)?.locations ?? []);
  readonly lowCount = computed(() => valueOf(this.res)?.lowCount ?? 0);
  readonly expiringCount = computed(() => valueOf(this.res)?.expiringCount ?? 0);
  /** Items needing attention (low, out or expiring), each counted once. */
  readonly attentionCount = computed(() => this.items().filter(needsAttention).length);
  readonly error = signal<string | null>(null);

  private readonly pending = new Map<string, { delta: number; timer: ReturnType<typeof setTimeout> }>();

  add(item: SaveStockItem) {
    return this.apply(this.http.post<StockList>('/api/stock', item));
  }

  update(id: string, changes: Record<string, unknown>) {
    return this.apply(this.http.patch<StockList>(`/api/stock/${id}`, changes));
  }

  remove(id: string) {
    return this.apply(this.http.delete<StockList>(`/api/stock/${id}`));
  }

  /**
   * −/+ buttons: the number changes on screen at once; taps are summed and sent as one delta after a
   * short pause, so quick taps (or two people tapping) add up instead of overwriting each other.
   */
  step(id: string, delta: number) {
    this.res.update((l) => l && {
      ...l,
      items: l.items.map((v) =>
        v.item.id !== id ? v : { ...v, item: { ...v.item, quantity: Math.max(0, Math.round((v.item.quantity + delta) * 100) / 100) } },
      ),
    });
    const p = this.pending.get(id);
    if (p) clearTimeout(p.timer);
    const total = (p?.delta ?? 0) + delta;
    this.pending.set(id, {
      delta: total,
      timer: setTimeout(() => {
        this.pending.delete(id);
        this.update(id, { delta: total });
      }, 600),
    });
  }

  /** Puts the item on the next supermarket list (by name). */
  toShopping(id: string): Promise<boolean> {
    return new Promise((resolve) =>
      this.http.post(`/api/stock/${id}/to-shopping`, {}).subscribe({
        next: () => resolve(true),
        error: () => {
          this.error.set('Não foi possível pôr o item na lista de compras.');
          resolve(false);
        },
      }),
    );
  }

  private apply(request: Observable<StockList>): Promise<boolean> {
    return new Promise((resolve) =>
      request.subscribe({
        next: (list) => {
          this.error.set(null);
          this.res.set(list);
          resolve(true);
        },
        error: (e) => {
          this.error.set(saveErrorMessage(e));
          this.res.reload();
          resolve(false);
        },
      }),
    );
  }
}

/** Low, out of stock, or expiring within 3 days (or already expired). */
export function needsAttention(v: StockView) {
  return v.status !== 'ok' || (v.daysToExpiry !== null && v.daysToExpiry <= 3);
}
