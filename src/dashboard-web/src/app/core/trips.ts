import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { MINUTE } from './dashboard-data';
import { ChecklistKind, SaveStop, SaveTrip, StopBooking, Trip, TripList, Transport } from './models';
import { pollingResource } from './polling-resource';
import { valueOf } from '../shared/resource-state';
import { saveErrorMessage } from './save-error';

/**
 * Trips shared by the Viagens tab and the "Próxima viagem" card on Hoje. Every change returns the
 * whole list from the API, which replaces the local copy, so both views always agree.
 */
@Injectable({ providedIn: 'root' })
export class Trips {
  private readonly http = inject(HttpClient);
  readonly res = pollingResource<TripList>('/api/trips', 2 * MINUTE);

  readonly upcoming = computed(() => valueOf(this.res)?.upcoming ?? []);
  readonly past = computed(() => valueOf(this.res)?.past ?? []);
  readonly next = computed<Trip | undefined>(() => this.upcoming()[0]);
  readonly error = signal<string | null>(null);

  create(trip: SaveTrip) {
    return this.apply(this.http.post<TripList>('/api/trips', trip));
  }

  update(id: string, trip: SaveTrip) {
    return this.apply(this.http.put<TripList>(`/api/trips/${id}`, trip));
  }

  remove(id: string) {
    return this.apply(this.http.delete<TripList>(`/api/trips/${id}`));
  }

  addStop(tripId: string, stop: SaveStop) {
    return this.apply(this.http.post<TripList>(`/api/trips/${tripId}/stops`, stop));
  }

  updateStop(tripId: string, stopId: string, stop: SaveStop) {
    return this.apply(this.http.put<TripList>(`/api/trips/${tripId}/stops/${stopId}`, stop));
  }

  /** Moves a stop to a new position; the list updates on screen first so ↑ / ↓ feel instant. */
  moveStop(tripId: string, stopId: string, index: number) {
    this.res.update((l) => l && {
      ...l,
      upcoming: l.upcoming.map((t) => {
        if (t.id !== tripId) return t;
        const stops = t.stops.filter((s) => s.id !== stopId);
        const moved = t.stops.find((s) => s.id === stopId);
        if (!moved) return t;
        stops.splice(Math.max(0, Math.min(index, stops.length)), 0, moved);
        return { ...t, stops };
      }),
    });
    return this.apply(this.http.post<TripList>(`/api/trips/${tripId}/stops/${stopId}/move`, { index }));
  }

  /** Ticks "Passagem ida" or "Hospedagem" on a stop, optimistically like the checklist. */
  setBooking(tripId: string, stopId: string, booking: StopBooking, done: boolean) {
    this.res.update((l) => l && {
      ...l,
      upcoming: l.upcoming.map((t) =>
        t.id !== tripId ? t : { ...t, stops: t.stops.map((s) => (s.id === stopId ? { ...s, [booking]: done } : s)) },
      ),
    });
    return this.apply(this.http.patch<TripList>(`/api/trips/${tripId}/stops/${stopId}/bookings`, { [booking]: done }));
  }

  removeStop(tripId: string, stopId: string) {
    return this.apply(this.http.delete<TripList>(`/api/trips/${tripId}/stops/${stopId}`));
  }

  addItem(tripId: string, text: string, kind: ChecklistKind) {
    return this.apply(this.http.post<TripList>(`/api/trips/${tripId}/items`, { text, kind }));
  }

  toggleItem(tripId: string, itemId: string, done: boolean) {
    // Optimistic: tick right away on the tablet, then take the server's list.
    this.res.update((l) => l && {
      ...l,
      upcoming: l.upcoming.map((t) =>
        t.id !== tripId ? t : { ...t, checklist: t.checklist.map((i) => (i.id === itemId ? { ...i, done } : i)) },
      ),
    });
    return this.apply(this.http.patch<TripList>(`/api/trips/${tripId}/items/${itemId}`, { done }));
  }

  removeItem(tripId: string, itemId: string) {
    return this.apply(this.http.delete<TripList>(`/api/trips/${tripId}/items/${itemId}`));
  }

  private apply(request: Observable<TripList>): Promise<boolean> {
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

export const TRANSPORTS: { value: Transport; label: string; symbol: string }[] = [
  { value: 'aviao', label: 'Avião', symbol: '✈' },
  { value: 'comboio', label: 'Comboio', symbol: '🚆' },
  { value: 'onibus', label: 'Ônibus', symbol: '🚌' },
  { value: 'carro', label: 'Carro', symbol: '🚗' },
  { value: 'barco', label: 'Barco', symbol: '⛴' },
  { value: 'outro', label: 'Outro', symbol: '→' },
];

export const BOOKINGS: { key: StopBooking; label: string }[] = [
  { key: 'outboundTicket', label: 'Passagem ida' },
  { key: 'lodging', label: 'Hospedagem' },
];

export function transportOf(t: Transport | null) {
  return TRANSPORTS.find((x) => x.value === t) ?? null;
}

/** "Porto ✈ Salvador 🚌 Ituberá": the itinerary in one line, each stop joined by how we get there. */
export function routeLine(trip: Trip) {
  return trip.stops
    .map((s, i) => (i === 0 ? s.place : `${transportOf(s.transport)?.symbol ?? '→'} ${s.place}`))
    .join(' ');
}

/** Checklist progress, e.g. { done: 3, total: 8, pct: 38 }. */
export function progress(trip: Trip) {
  const total = trip.checklist.length;
  const done = trip.checklist.filter((i) => i.done).length;
  return { done, total, pct: total ? Math.round((done / total) * 100) : 0 };
}
