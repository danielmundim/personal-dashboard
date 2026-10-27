import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { Clock, daysUntil, fmt } from '../core/clock';
import { Book, BookFormat, BookStatus, SaveBook } from '../core/models';
import { Books, FORMATS, STATUSES, bookProgress } from '../core/books';
import { Icon } from '../shared/icon';

/**
 * Leituras: books being read now (with the bookmark and a progress bar), the "quero ler" queue, and
 * what's been read, grouped by year with a star rating.
 */
@Component({
  selector: 'app-books-page',
  imports: [Icon, NgTemplateOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './books-page.html',
  styleUrl: './books-page.scss',
})
export class BooksPage {
  private readonly books = inject(Books);
  private readonly clock = inject(Clock);
  protected readonly statuses = STATUSES;
  protected readonly formats = FORMATS;
  protected readonly stars = [1, 2, 3, 4, 5];
  protected readonly error = this.books.error;
  protected readonly stats = this.books.stats;
  protected readonly loading = computed(() => !this.books.res.hasValue() && !this.books.res.error());

  /** 'new', a book id being edited, or null. */
  protected readonly editing = signal<string | null>(null);
  /** Status preselected in the "new" form, so "Adicionar à lista" opens as Quero ler. */
  protected readonly newStatus = signal<BookStatus>('lendo');
  protected readonly showFinished = signal(true);

  protected readonly reading = computed(() => this.books.reading().map((b) => this.view(b)));
  protected readonly queue = computed(() => this.books.queue().map((b) => this.view(b)));
  protected readonly finishedByYear = computed(() => {
    const groups = new Map<string, ReturnType<BooksPage['view']>[]>();
    for (const b of this.books.finished()) {
      const year = b.finishedOn?.slice(0, 4) ?? 'Sem data';
      groups.set(year, [...(groups.get(year) ?? []), this.view(b)]);
    }
    return [...groups].map(([year, books]) => ({ year, books }));
  });
  protected readonly editingBook = computed(() =>
    [...this.books.reading(), ...this.books.queue(), ...this.books.finished()].find((b) => b.id === this.editing()),
  );

  private view(b: Book) {
    const now = this.clock.now();
    const progress = bookProgress(b);
    const days = b.startedOn ? -daysUntil(b.startedOn, now) : null;
    return {
      ...b,
      progress,
      formatLabel: FORMATS.find((f) => f.value === b.format)?.label ?? '',
      since: b.startedOn ? `Desde ${fmt.dateOnlyShort(b.startedOn)}` : null,
      // Pages per day so far, and at that pace how long until the end. Only once there's some history.
      pace: progress && days && days >= 2 && b.currentPage > 0
        ? (() => {
            const perDay = b.currentPage / days;
            const left = Math.ceil(progress.left / perDay);
            return `~${Math.round(perDay)} pág./dia · termina em ${left === 1 ? '1 dia' : `${left} dias`}`;
          })()
        : null,
      finished: b.finishedOn ? fmt.dateOnlyShort(b.finishedOn) : null,
    };
  }

  protected newBook(status: BookStatus) {
    this.newStatus.set(status);
    this.editing.set('new');
  }

  protected async save(event: Event, form: HTMLFormElement) {
    event.preventDefault();
    const data = new FormData(form);
    const text = (k: string) => String(data.get(k) ?? '').trim() || null;
    const num = (k: string) => (text(k) === null ? null : Number(text(k)));
    const book: SaveBook = {
      title: text('title') ?? '',
      author: text('author'),
      status: (text('status') as BookStatus) ?? 'queroLer',
      format: (text('format') as BookFormat) ?? 'fisico',
      totalPages: num('totalPages'),
      currentPage: num('currentPage'),
      startedOn: text('startedOn'),
      finishedOn: text('finishedOn'),
      rating: num('rating'),
      notes: text('notes'),
    };
    if (!book.title) return;
    const id = this.editing();
    const ok = id && id !== 'new' ? await this.books.update(id, book) : await this.books.create(book);
    if (ok) this.editing.set(null);
  }

  protected bump(b: Book, delta: number) {
    const page = Math.max(0, Math.min(b.currentPage + delta, b.totalPages ?? Infinity));
    if (page !== b.currentPage) this.books.setPage(b.id, page);
  }

  protected setPage(b: Book, event: Event) {
    const input = event.target as HTMLInputElement;
    const page = Number(input.value);
    if (!Number.isFinite(page) || page === b.currentPage) return;
    this.books.setPage(b.id, page);
  }

  protected start(b: Book) {
    this.books.setStatus(b.id, 'lendo');
  }

  protected finish(b: Book) {
    this.books.setStatus(b.id, 'lido');
  }

  protected abandon(b: Book) {
    if (confirm(`Parar de ler "${b.title}"? Ele vai para os lidos como abandonado.`)) this.books.setStatus(b.id, 'abandonado');
  }

  protected rate(b: Book, rating: number) {
    this.books.setStatus(b.id, b.status, rating);
  }

  protected remove(b: Book) {
    if (confirm(`Excluir "${b.title}"?`)) {
      this.books.remove(b.id);
      this.editing.set(null);
    }
  }
}
