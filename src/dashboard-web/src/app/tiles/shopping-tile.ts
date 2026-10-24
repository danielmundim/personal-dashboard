import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { SECOND } from '../core/dashboard-data';
import { ShoppingItem, ShoppingList } from '../core/models';
import { Stock } from '../core/stock';
import { openShoppingListInWhatsApp } from '../core/whatsapp';
import { pollingResource } from '../core/polling-resource';
import { Icon } from '../shared/icon';
import { Tile } from '../shared/tile';
import { tileState, valueOf } from '../shared/resource-state';

/**
 * Two lists in one tile: the household's usual items (tap a chip when it runs out) and the resulting
 * "next trip" list, which is sent to WhatsApp before going to the supermarket. Stored by the API so
 * every device marks the same list; polls so a tap on the phone shows up on the wall within seconds.
 */
@Component({
  selector: 'app-shopping-tile',
  imports: [Tile, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './shopping-tile.html',
  styleUrl: './shopping-tile.scss',
})
export class ShoppingTile {
  private readonly http = inject(HttpClient);
  private readonly res = pollingResource<ShoppingList>('/api/shopping', 20 * SECOND);
  protected readonly state = computed(() => tileState(this.res));

  protected readonly items = computed(() => valueOf(this.res)?.items ?? []);
  /** Next trip, oldest first: the order things ran out. */
  protected readonly trip = computed(() =>
    this.items()
      .filter((i) => i.needed)
      .sort((a, b) => (a.neededSince ?? '').localeCompare(b.neededSince ?? '')),
  );
  protected readonly meta = computed(() => {
    const n = this.trip().length;
    return n === 0 ? 'Nada em falta' : n === 1 ? '1 em falta' : `${n} em falta`;
  });

  protected readonly query = signal('');
  protected readonly editing = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly sending = signal(false);

  /** Household chips, filtered by what's typed in the add box. */
  protected readonly chips = computed(() => {
    const q = normalize(this.query());
    return q ? this.items().filter((i) => normalize(i.name).includes(q)) : this.items();
  });
  /** Typed text that isn't an existing item: Enter adds it to the household list. */
  protected readonly isNew = computed(() => {
    const q = normalize(this.query());
    return q.length > 0 && !this.items().some((i) => normalize(i.name) === q);
  });

  protected onInput(event: Event) {
    this.query.set((event.target as HTMLInputElement).value);
  }

  /** Enter / +: adds the typed name to the household items. Picking it for the next trip is a tap on its chip. */
  protected add(event: Event, input: HTMLInputElement) {
    event.preventDefault();
    const name = input.value.trim();
    if (!name || !this.isNew()) return;
    this.apply(this.http.post<ShoppingList>('/api/shopping', { name }));
    input.value = '';
    this.query.set('');
    input.focus();
  }

  /**
   * − / + on a trip item. The count changes on screen immediately; the save waits for a short pause
   * so tapping + four times sends one request with 5, not four racing ones.
   */
  protected step(item: ShoppingItem, delta: number) {
    const quantity = Math.min(99, Math.max(1, item.quantity + delta));
    if (quantity === item.quantity) return;
    this.res.update((l) => l && { ...l, items: l.items.map((i) => (i.id === item.id ? { ...i, quantity } : i)) });
    clearTimeout(this.pendingSaves.get(item.id));
    this.pendingSaves.set(
      item.id,
      setTimeout(() => {
        this.pendingSaves.delete(item.id);
        this.apply(this.http.patch<ShoppingList>(`/api/shopping/${item.id}`, { quantity }));
      }, 500),
    );
  }

  private readonly pendingSaves = new Map<string, ReturnType<typeof setTimeout>>();

  protected toggle(item: ShoppingItem) {
    if (this.editing()) return;
    // Optimistic so the tap feels instant on a tablet; the server's list replaces it right after.
    const now = new Date().toISOString();
    this.res.update((l) => l && {
      ...l,
      items: l.items.map((i) =>
        i.id === item.id ? { ...i, needed: !i.needed, neededSince: i.needed ? null : now, quantity: 1 } : i,
      ),
    });
    this.apply(this.http.patch<ShoppingList>(`/api/shopping/${item.id}`, { needed: !item.needed }));
  }

  protected removeFromHousehold(item: ShoppingItem) {
    if (!confirm(`Remover "${item.name}" dos itens que costumamos comprar?`)) return;
    this.res.update((l) => l && { ...l, items: l.items.filter((i) => i.id !== item.id) });
    this.apply(this.http.delete<ShoppingList>(`/api/shopping/${item.id}`));
  }

  /** Empties the trip list; the API also adds what was bought to matching items in the Estoque. */
  protected clearTrip() {
    if (!confirm('Compras feitas? A lista da próxima compra é esvaziada e o que foi comprado entra no estoque.')) return;
    this.http.post<{ list: ShoppingList; stockUpdated: number }>('/api/shopping/trip/clear', {}).subscribe({
      next: (r) => {
        this.error.set(null);
        this.res.set(r.list);
        this.stock.res.reload(); // the purchases were just added there
        this.notice.set(
          r.stockUpdated === 0
            ? 'Lista esvaziada. Nenhum item comprado estava no estoque.'
            : `Lista esvaziada. ${r.stockUpdated} ${r.stockUpdated === 1 ? 'item atualizado' : 'itens atualizados'} no estoque.`,
        );
        setTimeout(() => this.notice.set(null), 6000);
      },
      error: () => this.error.set('Não foi possível concluir. Verifique a conexão e tente de novo.'),
    });
  }

  protected readonly notice = signal<string | null>(null);
  private readonly stock = inject(Stock);

  protected async sendToWhatsApp() {
    this.sending.set(true);
    const ok = await openShoppingListInWhatsApp(this.http);
    this.sending.set(false);
    if (!ok) this.error.set('Não foi possível preparar a mensagem do WhatsApp.');
  }

  private apply(request: Observable<ShoppingList>) {
    request.subscribe({
      next: (list) => {
        this.error.set(null);
        this.res.set(list);
      },
      error: () => {
        this.error.set('Não foi possível salvar. Verifique a conexão e tente de novo.');
        this.res.reload();
      },
    });
  }
}

/** Case- and accent-insensitive match, so "acucar" finds "Açúcar". */
function normalize(s: string) {
  return s.normalize('NFD').replace(/\p{Diacritic}/gu, '').toLowerCase().trim();
}
