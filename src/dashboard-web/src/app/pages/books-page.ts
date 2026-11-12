import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { Router } from '@angular/router';
import { Clock, daysUntil, fmt } from '../core/clock';
import { DashboardData } from '../core/dashboard-data';
import { Book, BookFormat, BookLookup, BookStatus, SaveBook } from '../core/models';
import { Books, CATEGORY_SUGGESTIONS, FORMATS, NO_CATEGORY, STATUSES, bookProgress, coverUrl, readingTime } from '../core/books';
import { valueOf } from '../shared/resource-state';
import { Icon } from '../shared/icon';

const LAYOUT_KEY = 'dashboard.books.layout';
type Layout = 'estantes' | 'lista';
type BookView = ReturnType<BooksPage['view']>;

/**
 * Leituras: books being read now (with the bookmark and a progress bar), the "quero ler" queue, and
 * what's been read with a star rating. The queue and the read books show either as shelves by
 * category (covers side by side; tapping one opens its line below the shelf) or as plain lists, the
 * read ones grouped by year. The choice is remembered per tablet. Every book has a reader; the filter
 * (?leitor=daniel) shows one person's shelf, so each tablet can be left on its owner.
 */
@Component({
  selector: 'app-books-page',
  imports: [Icon, NgTemplateOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './books-page.html',
  styleUrl: './books-page.scss',
})
export class BooksPage {
  /** ?leitor=<person id>; empty shows everyone. */
  readonly leitor = input<string>();

  private readonly books = inject(Books);
  private readonly clock = inject(Clock);
  private readonly router = inject(Router);
  private readonly data = inject(DashboardData);
  protected readonly statuses = STATUSES;
  protected readonly formats = FORMATS;
  protected readonly stars = [1, 2, 3, 4, 5];
  protected readonly error = this.books.error;
  protected readonly loading = computed(() => !this.books.res.hasValue() && !this.books.res.error());

  protected readonly people = computed(() => valueOf(this.data.config)?.people ?? []);
  /** The selected person, or null for "Todos" (also when the id in the URL isn't a known person). */
  protected readonly reader = computed(() => this.people().find((p) => p.id === this.leitor()) ?? null);

  /** Preselected in the form: the person whose shelf is open, or the first person under "Todos". */
  protected readonly defaultReader = computed(() => this.reader()?.id ?? this.people()[0]?.id ?? null);

  /** 'new', a book id being edited, or null. */
  protected readonly editing = signal<string | null>(null);
  /** Status preselected in the "new" form, so "Adicionar à lista" opens as Quero ler. */
  protected readonly newStatus = signal<BookStatus>('lendo');
  protected readonly showFinished = signal(true);
  protected readonly layout = signal<Layout>(readLayout());
  /** The book tapped on a shelf, whose line (with its actions) opens under that shelf. */
  protected readonly selected = signal<string | null>(null);
  /** ISBN lookup in the open form: what Open Library returned, or why it didn't. */
  protected readonly lookup = signal<{ busy: boolean; found: BookLookup | null; error: string | null }>({ busy: false, found: null, error: null });

  private readonly mine = (list: Book[]) => {
    const id = this.reader()?.id;
    // Books saved before readers existed have none; they stay on every shelf until edited.
    return id ? list.filter((b) => !b.readerId || b.readerId === id) : list;
  };

  protected readonly reading = computed(() => this.mine(this.books.reading()).map((b) => this.view(b)));
  protected readonly queue = computed(() => this.mine(this.books.queue()).map((b) => this.view(b)));
  protected readonly finishedByYear = computed(() => {
    const groups = new Map<string, ReturnType<BooksPage['view']>[]>();
    for (const b of this.mine(this.books.finished())) {
      const year = b.finishedOn?.slice(0, 4) ?? 'Sem data';
      groups.set(year, [...(groups.get(year) ?? []), this.view(b)]);
    }
    return [...groups].map(([year, books]) => ({ year, books }));
  });
  protected readonly queueShelves = computed(() => this.shelves(this.queue()));
  protected readonly finishedShelves = computed(() => this.shelves(this.mine(this.books.finished()).map((b) => this.view(b))));

  /** For the form's datalist: categories already on the shelves first, then the usual ones. */
  protected readonly categories = computed(() => {
    const all = [...this.books.reading(), ...this.books.queue(), ...this.books.finished()];
    const used = new Map(all.filter((b) => b.category).map((b) => [b.category!.toLocaleLowerCase('pt-BR'), b.category!]));
    for (const c of CATEGORY_SUGGESTIONS) if (!used.has(c.toLocaleLowerCase('pt-BR'))) used.set(c.toLocaleLowerCase('pt-BR'), c);
    return [...used.values()];
  });

  protected readonly editingBook = computed(() =>
    [...this.books.reading(), ...this.books.queue(), ...this.books.finished()].find((b) => b.id === this.editing()),
  );

  /** Header counters for this year, for the selected reader (or the whole house). */
  protected readonly stats = computed(() => {
    const year = String(this.clock.now().getFullYear());
    const read = this.mine(this.books.finished()).filter((b) => b.status === 'lido' && b.finishedOn?.startsWith(year));
    return { year, count: read.length, pages: read.reduce((sum, b) => sum + (b.totalPages ?? 0), 0) };
  });

  /** One shelf per category (case-insensitive), alphabetical, with "Sem categoria" at the end. */
  private shelves(books: BookView[]) {
    const groups = new Map<string, { name: string; books: BookView[] }>();
    for (const b of books) {
      const name = b.category ?? NO_CATEGORY;
      const key = name.toLocaleLowerCase('pt-BR');
      const g = groups.get(key) ?? { name, books: [] };
      g.books.push(b);
      groups.set(key, g);
    }
    const selected = this.selected();
    return [...groups.values()]
      .sort((a, b) => (a.name === NO_CATEGORY ? 1 : b.name === NO_CATEGORY ? -1 : a.name.localeCompare(b.name, 'pt-BR')))
      .map((g) => ({ ...g, selected: g.books.find((b) => b.id === selected) ?? null }));
  }

  private view(b: Book) {
    const now = this.clock.now();
    const progress = bookProgress(b);
    const days = b.startedOn ? -daysUntil(b.startedOn, now) : null;
    return {
      ...b,
      progress,
      cover: coverUrl(b),
      // Only worth saying whose book it is when looking at everyone's shelf.
      readerChip: this.reader() ? null : this.people().find((p) => p.id === b.readerId) ?? null,
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
      took: readingTime(b),
    };
  }

  protected setLayout(layout: Layout) {
    this.layout.set(layout);
    try {
      localStorage.setItem(LAYOUT_KEY, layout);
    } catch {
      // Private mode or blocked storage: the choice just isn't remembered.
    }
  }

  /** Tapping the open book again closes its line (and its form). */
  protected select(id: string) {
    const next = this.selected() === id ? null : id;
    if (this.editing() && this.editing() !== 'new') this.open(null);
    this.selected.set(next);
  }

  protected showReader(id: string | null) {
    this.router.navigate([], { queryParams: { leitor: id }, replaceUrl: true });
  }

  protected newBook(status: BookStatus) {
    this.newStatus.set(status);
    this.open('new');
  }

  protected open(id: string | null) {
    this.lookup.set({ busy: false, found: null, error: null });
    this.editing.set(id);
  }

  /** Looks the ISBN up and fills title, author and pages; the cover is downloaded by the API on save. */
  protected async findIsbn(form: HTMLFormElement) {
    const field = (name: string) => form.elements.namedItem(name) as HTMLInputElement;
    const isbn = field('isbn').value.trim();
    if (!isbn) return;
    this.lookup.set({ busy: true, found: null, error: null });
    const { book, error } = await this.books.lookup(isbn);
    this.lookup.set({ busy: false, found: book, error });
    if (!book) return;
    if (book.title) field('title').value = book.title;
    if (book.author) field('author').value = book.author;
    if (book.totalPages) field('totalPages').value = String(book.totalPages);
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
      readerId: text('readerId'),
      isbn: text('isbn'),
      category: text('category'),
    };
    if (!book.title) return;
    const id = this.editing();
    const ok = id && id !== 'new' ? await this.books.update(id, book) : await this.books.create(book);
    if (ok) this.open(null);
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
    this.books.setStatus(b.id, 'lendo', null, this.reader()?.id ?? null);
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
      this.open(null);
    }
  }
}

function readLayout(): Layout {
  try {
    return localStorage.getItem(LAYOUT_KEY) === 'lista' ? 'lista' : 'estantes';
  } catch {
    return 'estantes';
  }
}
