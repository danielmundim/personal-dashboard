import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Clock } from './core/clock';
import { DashboardData } from './core/dashboard-data';
import { Theme } from './core/theme';
import { valueOf } from './shared/resource-state';
import { Icon, IconName, weatherIcon } from './shared/icon';

interface NavTab {
  path: string;
  label: string;
  icon: IconName;
}

/** Shell: tab navigation (side rail, bottom bar on phones), the clock/weather header, and the active tab. */
@Component({
  selector: 'app-root',
  imports: [DecimalPipe, Icon, RouterOutlet, RouterLink, RouterLinkActive],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  protected readonly clock = inject(Clock);
  protected readonly theme = inject(Theme);
  private readonly data = inject(DashboardData);

  protected readonly tabs: NavTab[] = [
    { path: '/', label: 'Hoje', icon: 'home' },
    { path: '/fotos', label: 'Fotos', icon: 'image' },
    { path: '/viagens', label: 'Viagens', icon: 'plane' },
    { path: '/compras', label: 'Compras', icon: 'cart' },
  ];

  protected readonly config = computed(() => valueOf(this.data.config));
  protected readonly weather = computed(() => valueOf(this.data.weather));
  protected readonly weatherIcon = weatherIcon;

  /** Header badge: which tiles still show sample data, in words the family understands. */
  protected readonly mockSummary = computed(() => {
    const names: Record<string, string> = {
      weather: 'Tempo', transit: 'Ônibus', commute: 'Trajeto', calendar: 'Calendários',
      appointments: 'Clientes', cityEvents: 'Eventos', parties: 'Festas',
    };
    const mocks = this.config()?.mockWidgets ?? [];
    return mocks.length ? { count: mocks.length, list: mocks.map((m) => names[m] ?? m).join(', ') } : null;
  });
  protected readonly themeIcon = computed<IconName>(() =>
    ({ auto: 'themeAuto', light: 'sun', dark: 'moon' } as const)[this.theme.mode()],
  );
  protected readonly themeLabel = computed(() => ({ auto: 'automático', light: 'claro', dark: 'escuro' })[this.theme.mode()]);
}
