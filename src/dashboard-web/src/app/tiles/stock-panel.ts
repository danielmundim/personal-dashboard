import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { DecimalPipe, NgTemplateOutlet } from '@angular/common';
import { SaveStockItem, StockView } from '../core/models';
import { Stock, needsAttention } from '../core/stock';
import { Icon } from '../shared/icon';
import { tileState } from '../shared/resource-state';

export const UNITS = ['un', 'kg', 'g', 'L', 'ml', 'pacote', 'lata', 'garrafa', 'caixa', 'rolo', 'dúzia'];

/**
 * Estoque: everything we have at home, grouped by where it's kept. − / + adjust quantities, items at
 * or below their minimum (or expiring) are flagged, and one tap puts them on the next shopping list.
 */
@Component({
  selector: 'app-stock-panel',
  imports: [Icon, DecimalPipe, NgTemplateOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './stock-panel.html',
  styleUrl: './stock-panel.scss',
})
export class StockPanel {
  protected readonly stock = inject(Stock);
  protected readonly units = UNITS;
  protected readonly state = computed(() => tileState(this.stock.res));

  protected readonly query = signal('');
  /** null = everything; 'alert' = only what needs attention (low, out, expiring). */
  protected readonly filter = signal<'alert' | null>(null);
  protected readonly adding = signal(false);
  protected readonly editingId = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);

  protected readonly groups = computed(() => {
    const q = normalize(this.query());
    const onlyAlerts = this.filter() === 'alert';
    const visible = this.stock.items().filter(
      (v) => (!q || normalize(v.item.name).includes(q)) && (!onlyAlerts || needsAttention(v)),
    );
    return this.stock
      .locations()
      .map((location) => ({ location, items: visible.filter((v) => v.item.location === location) }))
      .filter((g) => g.items.length);
  });

  protected readonly total = computed(() => this.stock.items().length);

  protected badge(v: StockView): { text: string; tone: string } | null {
    if (v.daysToExpiry !== null && v.daysToExpiry < 0) return { text: 'Vencido', tone: 'rose' };
    if (v.status === 'out') return { text: 'Acabou', tone: 'rose' };
    if (v.daysToExpiry !== null && v.daysToExpiry <= 3)
      return { text: v.daysToExpiry === 0 ? 'Vence hoje' : `Vence em ${v.daysToExpiry} d`, tone: 'amber' };
    if (v.status === 'low') return { text: 'Acabando', tone: 'amber' };
    return null;
  }

  protected stepSize(unit: string) {
    return ['kg', 'L'].includes(unit) ? 0.5 : 1;
  }

  protected async save(event: Event, form: HTMLFormElement, id?: string) {
    event.preventDefault();
    const data = new FormData(form);
    const text = (k: string) => String(data.get(k) ?? '').trim();
    const num = (k: string) => (text(k) === '' ? null : Number(text(k).replace(',', '.')));
    const item: SaveStockItem = {
      name: text('name'),
      quantity: num('quantity') ?? 0,
      unit: text('unit') || null,
      location: text('location') || null,
      minQuantity: num('minQuantity'),
      expiresOn: text('expiresOn') || null,
    };
    if (!item.name || item.quantity < 0 || Number.isNaN(item.quantity)) return;

    const ok = id
      ? await this.stock.update(id, {
          ...item,
          clearMin: item.minQuantity === null,
          clearExpiry: item.expiresOn === null,
        })
      : await this.stock.add(item);
    if (!ok) return;
    if (id) this.editingId.set(null);
    else {
      form.reset();
      (form.querySelector('[name=name]') as HTMLInputElement | null)?.focus();
    }
  }

  protected remove(v: StockView) {
    if (confirm(`Remover "${v.item.name}" do estoque?`)) {
      this.stock.remove(v.item.id);
      this.editingId.set(null);
    }
  }

  protected async toShopping(v: StockView) {
    if (await this.stock.toShopping(v.item.id)) this.flash(`"${v.item.name}" foi para a próxima compra.`);
  }

  private flash(text: string) {
    this.notice.set(text);
    setTimeout(() => this.notice.set(null), 4000);
  }
}

function normalize(s: string) {
  return s.normalize('NFD').replace(/\p{Diacritic}/gu, '').toLowerCase().trim();
}
