import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { MINUTE } from './dashboard-data';
import { ChecklistKind, SaveTrip, Trip, TripList } from './models';
import { pollingResource } from './polling-resource';
import { valueOf } from '../shared/resource-state';

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
        error: () => {
          this.error.set('Não foi possível salvar. Verifique a conexão e tente de novo.');
          this.res.reload();
          resolve(false);
        },
      }),
    );
  }
}

/** Checklist progress, e.g. { done: 3, total: 8, pct: 38 }. */
export function progress(trip: Trip) {
  const total = trip.checklist.length;
  const done = trip.checklist.filter((i) => i.done).length;
  return { done, total, pct: total ? Math.round((done / total) * 100) : 0 };
}
