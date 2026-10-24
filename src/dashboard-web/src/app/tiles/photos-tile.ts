import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { MINUTE } from '../core/dashboard-data';
import { PhotoList } from '../core/models';
import { pollingResource } from '../core/polling-resource';
import { RouterLink } from '@angular/router';
import { Icon } from '../shared/icon';
import { Tile } from '../shared/tile';
import { tileState, valueOf } from '../shared/resource-state';

/** Slideshow of our own photos, uploaded from any device. Cross-fades between two stacked images. */
@Component({
  selector: 'app-photos-tile',
  imports: [Tile, Icon, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-tile title="Fotos" icon="image" accent="sky" [state]="state()" [meta]="meta()">
      <div class="frame">
        @for (p of photos(); track p.id; let i = $index) {
          <img [src]="p.url" alt="" [class.shown]="i === index()" [attr.loading]="i === index() ? 'eager' : 'lazy'" />
        }
        @if (current(); as c) {
          @if (c.album || c.caption) {
            <span class="label">{{ c.caption ?? '' }}{{ c.caption && c.album ? ' · ' : '' }}{{ c.album ?? '' }}</span>
          }
        }
        @if (photos().length > 1) {
          <button type="button" class="nav prev" (click)="step(-1)" aria-label="Foto anterior"><app-icon name="chevronLeft" [size]="20" /></button>
          <button type="button" class="nav next" (click)="step(1)" aria-label="Próxima foto"><app-icon name="chevronRight" [size]="20" /></button>
          <div class="dots">
            @for (p of photos(); track p.id; let i = $index) {
              <span [class.on]="i === index()"></span>
            }
          </div>
        }
      </div>
      <div class="bar">
        @if (isSample()) {
          <span class="muted hint">Mostrando imagens de exemplo. Envie suas fotos para substituí-las.</span>
        }
        <a class="all" routerLink="/fotos">Ver todas</a>
        <label class="upload" [class.busy]="uploading()">
          <input type="file" accept="image/jpeg,image/png,image/webp,image/gif" multiple (change)="upload($event)" [disabled]="uploading()" />
          <app-icon name="upload" [size]="15" />{{ uploading() ? 'Enviando…' : 'Enviar fotos' }}
        </label>
        @if (!isSample() && current()) {
          <button type="button" class="delete" (click)="deleteCurrent()" aria-label="Excluir esta foto"><app-icon name="trash" [size]="15" /></button>
        }
      </div>
      @if (error()) {
        <p class="error">{{ error() }}</p>
      }
    </app-tile>
  `,
  styleUrl: './photos-tile.scss',
})
export class PhotosTile {
  private readonly http = inject(HttpClient);
  private readonly res = pollingResource<PhotoList>('/api/photos', 5 * MINUTE);
  protected readonly state = computed(() => tileState(this.res));
  protected readonly photos = computed(() => valueOf(this.res)?.photos ?? []);
  protected readonly isSample = computed(() => this.photos().some((p) => p.sample));
  protected readonly index = signal(0);
  protected readonly current = computed(() => this.photos()[this.index()]);
  protected readonly meta = computed(() => (this.photos().length ? `${this.index() + 1} / ${this.photos().length}` : ''));
  protected readonly uploading = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    // Restart the rotation timer whenever the interval or the photo count changes, or after a manual step.
    let timer: ReturnType<typeof setInterval> | undefined;
    effect(() => {
      const seconds = valueOf(this.res)?.rotateSeconds ?? 5;
      const count = this.photos().length;
      this.index(); // manual navigation resets the countdown
      clearInterval(timer);
      if (count > 1) timer = setInterval(() => this.index.update((i) => (i + 1) % count), seconds * 1000);
    });
    inject(DestroyRef).onDestroy(() => clearInterval(timer));

    effect(() => {
      // Keep the index valid when photos are added or removed elsewhere.
      const count = this.photos().length;
      if (count && this.index() >= count) this.index.set(0);
    });
  }

  protected step(delta: number) {
    const count = this.photos().length;
    this.index.update((i) => (i + delta + count) % count);
  }

  protected upload(event: Event) {
    const input = event.target as HTMLInputElement;
    const files = Array.from(input.files ?? []);
    if (!files.length) return;
    const form = new FormData();
    files.forEach((f) => form.append('files', f, f.name));

    this.uploading.set(true);
    this.error.set(null);
    this.http.post('/api/photos', form).subscribe({
      next: () => {
        this.uploading.set(false);
        input.value = '';
        this.index.set(0); // newest first, so show what was just uploaded
        this.res.reload();
      },
      error: (e) => {
        this.uploading.set(false);
        input.value = '';
        this.error.set(typeof e?.error === 'string' ? e.error : 'Falha no envio. Use JPG, PNG, WebP ou GIF de até 25 MB.');
      },
    });
  }

  protected deleteCurrent() {
    const photo = this.current();
    if (!photo || !confirm('Excluir esta foto do painel?')) return;
    this.http.delete(`/api/photos/${encodeURIComponent(photo.id)}`).subscribe({
      next: () => this.res.reload(),
      error: () => this.error.set('Não foi possível excluir a foto.'),
    });
  }
}
