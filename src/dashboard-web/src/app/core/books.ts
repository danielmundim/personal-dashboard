import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { MINUTE } from './dashboard-data';
import { Book, BookFormat, BookList, BookLookup, BookStatus, SaveBook } from './models';
import { pollingResource } from './polling-resource';
import { valueOf } from '../shared/resource-state';
import { saveErrorMessage } from './save-error';

/**
 * Books for the Leituras tab. Every change returns the whole list from the API, which replaces the
 * local copy, same as Trips.
 */
@Injectable({ providedIn: 'root' })
export class Books {
  private readonly http = inject(HttpClient);
  readonly res = pollingResource<BookList>('/api/books', 5 * MINUTE);

  readonly reading = computed(() => valueOf(this.res)?.reading ?? []);
  readonly queue = computed(() => valueOf(this.res)?.queue ?? []);
  readonly finished = computed(() => valueOf(this.res)?.finished ?? []);
  readonly error = signal<string | null>(null);

  create(book: SaveBook) {
    return this.apply(this.http.post<BookList>('/api/books', book));
  }

  update(id: string, book: SaveBook) {
    return this.apply(this.http.put<BookList>(`/api/books/${id}`, book));
  }

  /** Moves the bookmark; the bar moves on screen first so +10 feels instant. */
  setPage(id: string, currentPage: number) {
    this.res.update((l) => l && { ...l, reading: l.reading.map((b) => (b.id === id ? { ...b, currentPage } : b)) });
    return this.apply(this.http.patch<BookList>(`/api/books/${id}/progress`, { currentPage }));
  }

  /** `readerId` claims a book that has no reader yet (saved before readers existed); never reassigns one. */
  setStatus(id: string, status: BookStatus, rating: number | null = null, readerId: string | null = null) {
    return this.apply(this.http.patch<BookList>(`/api/books/${id}/status`, { status, rating, readerId }));
  }

  /** Open Library details for the form; null when the ISBN is unknown there or the lookup failed. */
  lookup(isbn: string): Promise<{ book: BookLookup | null; error: string | null }> {
    return new Promise((resolve) =>
      this.http.get<BookLookup>(`/api/books/lookup/${encodeURIComponent(isbn)}`).subscribe({
        next: (book) => resolve({ book, error: null }),
        error: (e) =>
          resolve({
            book: null,
            error: e.status === 404 ? 'O Open Library não conhece esse ISBN. Preencha à mão.'
              : e.status === 502 ? 'Não foi possível falar com o Open Library agora.'
              : saveErrorMessage(e),
          }),
      }),
    );
  }

  remove(id: string) {
    return this.apply(this.http.delete<BookList>(`/api/books/${id}`));
  }

  private apply(request: Observable<BookList>): Promise<boolean> {
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

export const STATUSES: { value: BookStatus; label: string }[] = [
  { value: 'queroLer', label: 'Quero ler' },
  { value: 'lendo', label: 'Lendo' },
  { value: 'lido', label: 'Lido' },
  { value: 'abandonado', label: 'Abandonado' },
];

export const FORMATS: { value: BookFormat; label: string }[] = [
  { value: 'fisico', label: 'Físico' },
  { value: 'ebook', label: 'E-book' },
  { value: 'audiobook', label: 'Audiolivro' },
];

/** Served by the API from the downloaded copy; the ISBN busts the browser cache when the edition changes. */
export function coverUrl(b: Book) {
  return b.hasCover ? `/api/books/${b.id}/cover?v=${b.isbn}` : null;
}

/** Reading progress, e.g. { pct: 54, left: 122 }; null without a page count. */
export function bookProgress(b: Book) {
  if (!b.totalPages) return null;
  return { pct: Math.min(100, Math.round((b.currentPage / b.totalPages) * 100)), left: Math.max(0, b.totalPages - b.currentPage) };
}
