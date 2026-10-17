import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { Icon, IconName } from './icon';

/**
 * Card frame shared by every widget: header (icon, title, optional meta text) and a body slot.
 * `state` lets a tile show loading/error without each widget re-implementing it.
 */
@Component({
  selector: 'app-tile',
  imports: [Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[class.tile]': 'true', '[attr.data-accent]': 'accent()' },
  template: `
    <header class="tile-head">
      <span class="tile-icon"><app-icon [name]="icon()" [size]="18" /></span>
      <h2>{{ title() }}</h2>
      @if (meta()) {
        <span class="tile-meta">{{ meta() }}</span>
      }
    </header>
    @switch (state()) {
      @case ('loading') {
        <div class="tile-skeleton" aria-busy="true"><span></span><span></span><span></span></div>
      }
      @case ('error') {
        <p class="tile-error">Não foi possível carregar agora. Tentando de novo…</p>
      }
      @default {
        <div class="tile-body"><ng-content /></div>
      }
    }
  `,
  styleUrl: './tile.scss',
})
export class Tile {
  readonly title = input.required<string>();
  readonly icon = input.required<IconName>();
  readonly meta = input<string>();
  readonly accent = input<'sky' | 'amber' | 'green' | 'violet' | 'rose' | 'teal'>('sky');
  readonly state = input<'ready' | 'loading' | 'error'>('ready');
}
