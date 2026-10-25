import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { RouterLink } from '@angular/router';
import { Clock, daysUntil, fmt } from '../core/clock';
import { ChecklistKind, SaveStop, SaveTrip, StopBooking, Transport, Trip, TripStop } from '../core/models';
import { BOOKINGS, TRANSPORTS, Trips, progress, routeLine, transportOf } from '../core/trips';
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
  protected readonly transports = TRANSPORTS;
  protected readonly transportOf = transportOf;
  /** "tripId:stopId" being edited, or "tripId:new" for the add form. */
  protected readonly editingStop = signal<string | null>(null);
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
      ongoing: days <= 0 && (!t.endDate || daysUntil(end, now) >= 0),
      dates: t.endDate
        ? `${fmt.dateOnlyShort(t.startDate)} – ${fmt.dateOnlyShort(t.endDate)}`
        : `${fmt.dateOnlyShort(t.startDate)} · volta em aberto`,
      length: !t.endDate ? null : length === 1 ? '1 dia' : `${length} dias`,
      tripDays: itineraryDays(t),
      route: routeLine(t),
      stopViews: t.stops.map((s, i) => ({ ...this.stopView(s), outOfOrder: datedBeforePrevious(t.stops, i) })),
      hasDateWarning: t.stops.some((_, i) => datedBeforePrevious(t.stops, i)),
      year: t.startDate.slice(0, 4),
      progress: progress(t),
      groups: KINDS.map((k) => ({ ...k, items: t.checklist.filter((i) => i.kind === k.kind) })).filter((g) => g.items.length),
    };
  }

  private stopView(s: TripStop) {
    const nights = s.arrive && s.depart ? Math.round((Date.parse(s.depart) - Date.parse(s.arrive)) / 86_400_000) : null;
    const when =
      s.arrive && s.depart ? `${fmt.dateOnlyShort(s.arrive)} – ${fmt.dateOnlyShort(s.depart)}`
      : s.arrive ? `Chegada ${fmt.dateOnlyShort(s.arrive)}`
      : s.depart ? `Saída ${fmt.dateOnlyShort(s.depart)}`
      : 'Datas em aberto';
    // Lodging only makes sense for a known stay: not for a same-day stop, nor while the departure is open.
    const bookings = BOOKINGS.filter((b) => b.key !== 'lodging' || (!!s.depart && nights !== 0));
    return { ...s, when, bookings, nights: nights === null ? null : nights === 1 ? '1 noite' : `${nights} noites` };
  }

  protected async saveStop(event: Event, form: HTMLFormElement, t: Trip, stopId?: string) {
    event.preventDefault();
    const data = new FormData(form);
    const text = (k: string) => String(data.get(k) ?? '').trim() || null;
    const stop: SaveStop = {
      place: text('place') ?? '',
      arrive: text('arrive'),
      depart: text('depart'),
      transport: (text('transport') as Transport | null) ?? null,
      notes: text('notes'),
    };
    if (!stop.place) return;
    const ok = stopId ? await this.trips.updateStop(t.id, stopId, stop) : await this.trips.addStop(t.id, stop);
    if (!ok) return;
    if (stopId) this.editingStop.set(null);
    else {
      form.reset();
      (form.querySelector('[name=place]') as HTMLInputElement | null)?.focus();
    }
  }

  protected moveStop(t: Trip, index: number, delta: number) {
    const target = index + delta;
    if (target < 0 || target >= t.stops.length) return;
    this.trips.moveStop(t.id, t.stops[index].id, target);
  }

  protected setBooking(t: Trip, s: TripStop, booking: StopBooking, done: boolean) {
    this.trips.setBooking(t.id, s.id, booking, done);
  }

  protected removeStop(t: Trip, s: TripStop) {
    if (confirm(`Tirar "${s.place}" do roteiro?`)) {
      this.trips.removeStop(t.id, s.id);
      this.editingStop.set(null);
    }
  }

  protected async save(event: Event, form: HTMLFormElement) {
    event.preventDefault();
    const data = new FormData(form);
    const text = (k: string) => String(data.get(k) ?? '').trim() || null;
    const trip: SaveTrip = {
      name: text('name') ?? '',
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
    this.trips.update(t.id, { name: t.name, startDate: t.startDate, endDate: t.endDate, notes });
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

/**
 * Trip length from the itinerary: first known date to last known date, counting both days
 * (11 → 27 dez = 17). Uses the return date as the end when it's later. Null with fewer than one
 * dated stop, or when the stops have only one date in total.
 */
function itineraryDays(t: Trip): number | null {
  const dates = t.stops.flatMap((s) => [s.arrive, s.depart]).filter((d): d is string => !!d);
  if (t.endDate) dates.push(t.endDate);
  if (dates.length < 2) return null;
  const sorted = dates.sort();
  return Math.round((Date.parse(sorted[sorted.length - 1]) - Date.parse(sorted[0])) / 86_400_000) + 1;
}

/**
 * True when a stop's earliest date is before the latest date of any earlier stop. The itinerary order
 * is the travel order, so dates going backwards are almost always a typo (e.g. the wrong year), and
 * they also throw off the trip length.
 */
function datedBeforePrevious(stops: TripStop[], index: number): boolean {
  const own = [stops[index].arrive, stops[index].depart].filter((d): d is string => !!d).sort()[0];
  if (!own) return false;
  const before = stops
    .slice(0, index)
    .flatMap((s) => [s.arrive, s.depart])
    .filter((d): d is string => !!d)
    .sort();
  return before.length > 0 && own < before[before.length - 1];
}
