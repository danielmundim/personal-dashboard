import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { RouterLink } from '@angular/router';
import { Clock, daysUntil, fmt } from '../core/clock';
import { ChecklistKind, SaveTrip, Trip } from '../core/models';
import { Trips, progress } from '../core/trips';
import { Icon } from '../shared/icon';

const KINDS: { kind: ChecklistKind; label: string }[] = [
  { kind: 'comprar', label: 'Comprar' },
  { kind: 'levar', label: 'Levar' },
  { kind: 'fazer', label: 'Fazer' },
];

/**
 * Viagens: each upcoming trip with its dates, planning notes and a checklist (what to buy, pack or
 * do). Past trips stay below with a link to their photo album.
 */
@Component({
  selector: 'app-trips-page',
  imports: [Icon, RouterLink, NgTemplateOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './trips-page.html',
  styleUrl: './trips-page.scss',
})
export class TripsPage {
  private readonly trips = inject(Trips);
  private readonly clock = inject(Clock);
  protected readonly kinds = KINDS;
  protected readonly fmt = fmt;
  protected readonly error = this.trips.error;
  protected readonly loading = computed(() => !this.trips.res.hasValue() && !this.trips.res.error());

  /** 'new', a trip id being edited, or null. */
  protected readonly editing = signal<string | null>(null);
  protected readonly showPast = signal(false);

  protected readonly upcoming = computed(() => this.trips.upcoming().map((t) => this.view(t)));
  protected readonly past = computed(() => this.trips.past().map((t) => this.view(t)));
  protected readonly editingTrip = computed(() => this.trips.upcoming().concat(this.trips.past()).find((t) => t.id === this.editing()));

  private view(t: Trip) {
    const now = this.clock.now();
    const days = daysUntil(t.startDate, now);
    const end = t.endDate ?? t.startDate;
    const length = Math.round((Date.parse(end) - Date.parse(t.startDate)) / 86_400_000) + 1;
    return {
      ...t,
      days,
      ongoing: days <= 0 && daysUntil(end, now) >= 0,
      dates: t.endDate ? `${fmt.dateOnlyShort(t.startDate)} – ${fmt.dateOnlyShort(t.endDate)}` : fmt.dateOnlyShort(t.startDate),
      length: length === 1 ? '1 dia' : `${length} dias`,
      year: t.startDate.slice(0, 4),
      progress: progress(t),
      groups: KINDS.map((k) => ({ ...k, items: t.checklist.filter((i) => i.kind === k.kind) })).filter((g) => g.items.length),
    };
  }

  protected async save(event: Event, form: HTMLFormElement) {
    event.preventDefault();
    const data = new FormData(form);
    const text = (k: string) => String(data.get(k) ?? '').trim() || null;
    const trip: SaveTrip = {
      name: text('name') ?? '',
      destination: text('destination'),
      startDate: text('startDate') ?? '',
      endDate: text('endDate'),
      notes: text('notes'),
    };
    if (!trip.name || !trip.startDate) return;
    const id = this.editing();
    const ok = id && id !== 'new' ? await this.trips.update(id, trip) : await this.trips.create(trip);
    if (ok) this.editing.set(null);
  }

  /** Planning notes save when the field loses focus, so typing never fights the 2-minute refresh. */
  protected saveNotes(t: Trip, event: Event) {
    const notes = (event.target as HTMLTextAreaElement).value.trim() || null;
    if (notes === (t.notes ?? null)) return;
    this.trips.update(t.id, { name: t.name, destination: t.destination, startDate: t.startDate, endDate: t.endDate, notes });
  }

  protected remove(t: Trip) {
    if (confirm(`Excluir a viagem "${t.name}" e o checklist dela?`)) this.trips.remove(t.id);
  }

  protected addItem(event: Event, t: Trip, input: HTMLInputElement, kind: HTMLSelectElement) {
    event.preventDefault();
    const text = input.value.trim();
    if (!text) return;
    this.trips.addItem(t.id, text, kind.value as ChecklistKind);
    input.value = '';
    input.focus();
  }

  protected toggle(t: Trip, itemId: string, done: boolean) {
    this.trips.toggleItem(t.id, itemId, done);
  }

  protected removeItem(t: Trip, itemId: string) {
    this.trips.removeItem(t.id, itemId);
  }
}
