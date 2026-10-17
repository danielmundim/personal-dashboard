import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { SECOND } from '../core/dashboard-data';
import { ShoppingItem, ShoppingList } from '../core/models';
import { pollingResource } from '../core/polling-resource';
import { Icon } from '../shared/icon';
import { Tile } from '../shared/tile';
import { tileState, valueOf } from '../shared/resource-state';

/**
 * Shared shopping list, stored by the API so every device edits the same list. Polls so an item
 * added on a phone shows up on the wall within seconds.
 */
@Component({
  selector: 'app-shopping-tile',
  imports: [Tile, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-tile title="Lista de compras" icon="cart" accent="green" [state]="state()" [meta]="meta()">
      <form class="add" (submit)="add($event, name, qty)">
        <input #name type="text" placeholder="Adicionar item…" maxlength="100" autocomplete="off" aria-label="Item" />
        <input #qty type="text" placeholder="Qtd" maxlength="20" autocomplete="off" aria-label="Quantidade" class="qty" />
        <button type="submit" aria-label="Adicionar item"><app-icon name="plus" [size]="18" /></button>
      </form>

      <ul>
        @for (item of items(); track item.id) {
          <li [class.done]="item.done">
            <label>
              <input type="checkbox" [checked]="item.done" (change)="toggle(item)" />
              <span class="box" aria-hidden="true"></span>
              <span class="name">{{ item.name }}</span>
              @if (item.quantity) {
                <span class="q">{{ item.quantity }}</span>
              }
            </label>
            <button type="button" class="remove" (click)="remove(item)" [attr.aria-label]="'Remover ' + item.name">
              <app-icon name="x" [size]="14" />
            </button>
          </li>
        } @empty {
          <li class="empty muted">A lista está vazia.</li>
        }
      </ul>

      @if (doneCount() > 0) {
        <button type="button" class="clear" (click)="clearDone()">
          <app-icon name="trash" [size]="14" />Limpar {{ doneCount() }} {{ doneCount() === 1 ? 'marcado' : 'marcados' }}
        </button>
      }
      @if (error()) {
        <p class="error">{{ error() }}</p>
      }
    </app-tile>
  `,
  styleUrl: './shopping-tile.scss',
})
export class ShoppingTile {
  private readonly http = inject(HttpClient);
  private readonly res = pollingResource<ShoppingList>('/api/shopping', 20 * SECOND);
  protected readonly state = computed(() => tileState(this.res));
  protected readonly items = computed(() => valueOf(this.res)?.items ?? []);
  protected readonly doneCount = computed(() => this.items().filter((i) => i.done).length);
  protected readonly meta = computed(() => {
    const left = this.items().length - this.doneCount();
    return left === 1 ? '1 item' : `${left} itens`;
  });
  protected readonly error = signal<string | null>(null);

  protected add(event: Event, name: HTMLInputElement, qty: HTMLInputElement) {
    event.preventDefault();
    const value = name.value.trim();
    if (!value) return;
    this.apply(this.http.post<ShoppingList>('/api/shopping', { name: value, quantity: qty.value.trim() || null }));
    name.value = '';
    qty.value = '';
    name.focus();
  }

  protected toggle(item: ShoppingItem) {
    // Optimistic: flip locally so the tap feels instant, then take the server's list.
    this.res.update((l) => l && { ...l, items: l.items.map((i) => (i.id === item.id ? { ...i, done: !i.done } : i)) });
    this.apply(this.http.patch<ShoppingList>(`/api/shopping/${item.id}`, { done: !item.done }));
  }

  protected remove(item: ShoppingItem) {
    this.res.update((l) => l && { ...l, items: l.items.filter((i) => i.id !== item.id) });
    this.apply(this.http.delete<ShoppingList>(`/api/shopping/${item.id}`));
  }

  protected clearDone() {
    this.apply(this.http.delete<ShoppingList>('/api/shopping/done'));
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
