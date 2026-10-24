import { ChangeDetectionStrategy, Component, HostListener, computed, effect, inject, input, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { MINUTE } from '../core/dashboard-data';
import { Photo, PhotoList } from '../core/models';
import { pollingResource } from '../core/polling-resource';
import { Icon } from '../shared/icon';
import { valueOf } from '../shared/resource-state';

const NO_ALBUM = '__none__';

/**
 * Fotos: every photo we've uploaded, grouped into albums for moments and trips. Upload straight into
 * an album, filter by album, and open a photo to edit its caption or album, or delete it. The Hoje
 * slideshow uses the same photos.
 */
@Component({
  selector: 'app-photos-page',
  imports: [Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './photos-page.html',
  styleUrl: './photos-page.scss',
})
export class PhotosPage {
  /** ?album=… from the Viagens tab ("Fotos desta viagem"). */
  readonly album = input<string>();

  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly res = pollingResource<PhotoList>('/api/photos', 5 * MINUTE);

  protected readonly noAlbum = NO_ALBUM;
  protected readonly loading = computed(() => !this.res.hasValue() && !this.res.error());
  protected readonly all = computed(() => valueOf(this.res)?.photos ?? []);
  protected readonly isSample = computed(() => this.all().some((p) => p.sample));
  protected readonly albums = computed(() => valueOf(this.res)?.albums ?? []);
  protected readonly withoutAlbum = computed(() => this.all().filter((p) => !p.album).length);

  /** null = all photos; NO_ALBUM = photos without an album; otherwise an album name. */
  protected readonly filter = signal<string | null>(null);
  protected readonly photos = computed(() => {
    const f = this.filter();
    if (f === null) return this.all();
    if (f === NO_ALBUM) return this.all().filter((p) => !p.album);
    return this.all().filter((p) => p.album?.toLocaleLowerCase() === f.toLocaleLowerCase());
  });

  protected readonly uploading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly openIndex = signal<number | null>(null);
  protected readonly open = computed(() => (this.openIndex() === null ? null : this.photos()[this.openIndex()!] ?? null));

  constructor() {
    // Arriving from a trip ("Fotos desta viagem") pre-selects that album, even if it has no photos yet.
    effect(() => {
      const a = this.album();
      if (a) this.filter.set(a);
    });
  }

  protected select(filter: string | null) {
    this.filter.set(filter);
    this.openIndex.set(null);
    this.router.navigate([], { queryParams: { album: filter && filter !== NO_ALBUM ? filter : null }, replaceUrl: true });
  }

  /** Album to suggest in the upload form: the one being viewed, if any. */
  protected readonly uploadAlbum = computed(() => {
    const f = this.filter();
    return f && f !== NO_ALBUM ? f : '';
  });

  protected upload(event: Event, albumInput: HTMLInputElement, captionInput: HTMLInputElement) {
    const input = event.target as HTMLInputElement;
    const files = Array.from(input.files ?? []);
    if (!files.length) return;
    const form = new FormData();
    files.forEach((f) => form.append('files', f, f.name));
    const album = albumInput.value.trim();
    if (album) form.append('album', album);
    if (captionInput.value.trim()) form.append('caption', captionInput.value.trim());

    this.uploading.set(true);
    this.error.set(null);
    this.http.post('/api/photos', form).subscribe({
      next: () => {
        this.uploading.set(false);
        input.value = '';
        captionInput.value = '';
        if (album) this.select(album);
        this.res.reload();
      },
      error: (e) => {
        this.uploading.set(false);
        input.value = '';
        this.error.set(typeof e?.error === 'string' ? e.error : 'Falha no envio. Use JPG, PNG, WebP ou GIF de até 25 MB.');
      },
    });
  }

  protected show(i: number) {
    this.openIndex.set(i);
  }

  protected close() {
    this.openIndex.set(null);
  }

  protected step(delta: number) {
    const n = this.photos().length;
    const i = this.openIndex();
    if (i === null || !n) return;
    this.openIndex.set((i + delta + n) % n);
  }

  @HostListener('document:keydown', ['$event'])
  protected onKey(e: KeyboardEvent) {
    if (this.openIndex() === null) return;
    if (e.key === 'Escape') this.close();
    if (e.key === 'ArrowRight') this.step(1);
    if (e.key === 'ArrowLeft') this.step(-1);
  }

  protected saveDetails(photo: Photo, album: HTMLInputElement, caption: HTMLInputElement) {
    const body = { album: album.value.trim(), caption: caption.value.trim() };
    this.http.patch(`/api/photos/${encodeURIComponent(photo.id)}`, body).subscribe({
      next: () => {
        this.error.set(null);
        this.res.reload();
      },
      error: () => this.error.set('Não foi possível salvar os detalhes da foto.'),
    });
  }

  protected remove(photo: Photo) {
    if (!confirm('Excluir esta foto do painel?')) return;
    this.http.delete(`/api/photos/${encodeURIComponent(photo.id)}`).subscribe({
      next: () => {
        this.close();
        this.res.reload();
      },
      error: () => this.error.set('Não foi possível excluir a foto.'),
    });
  }
}
