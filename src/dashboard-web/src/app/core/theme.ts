import { Injectable, effect, signal } from '@angular/core';

export type ThemeMode = 'auto' | 'light' | 'dark';

const STORAGE_KEY = 'dashboard.theme';

/** Auto follows the OS; light/dark pin it. Choice is per device (wall vs tablet vs laptop). */
@Injectable({ providedIn: 'root' })
export class Theme {
  readonly mode = signal<ThemeMode>(this.load());

  constructor() {
    effect(() => {
      const mode = this.mode();
      const root = document.documentElement;
      if (mode === 'auto') root.removeAttribute('data-theme');
      else root.setAttribute('data-theme', mode);
      try {
        localStorage.setItem(STORAGE_KEY, mode);
      } catch {
        // Storage can be unavailable (private mode); the theme still applies for this visit.
      }
    });
  }

  cycle() {
    const order: ThemeMode[] = ['auto', 'light', 'dark'];
    this.mode.update((m) => order[(order.indexOf(m) + 1) % order.length]);
  }

  private load(): ThemeMode {
    try {
      const v = localStorage.getItem(STORAGE_KEY);
      return v === 'light' || v === 'dark' ? v : 'auto';
    } catch {
      return 'auto';
    }
  }
}
