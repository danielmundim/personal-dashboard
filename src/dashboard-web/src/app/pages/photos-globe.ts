import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  afterNextRender,
  effect,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import type { Feature, FeatureCollection, Geometry } from 'geojson';
import type { GlobeInstance } from 'globe.gl';
import { PlaceSummary } from '../core/models';
import { Theme } from '../core/theme';
import { Icon } from '../shared/icon';

/** What a tap on the globe picked: a city pin, or a whole country that has photos. */
export interface PlacePick {
  country: string;
  city: string | null;
}

type Country = Feature<Geometry, { name: string }> & { visited?: boolean };

/** Camera distance in globe radii: the whole Earth, and how far in a tap on a pin flies. */
const FAR = 2.3;
const NEAR = 0.45;

/**
 * 3D globe of where our photos were taken: countries with photos are tinted, and each city is a pin
 * with its cover photo and count. Drag to turn, pinch / scroll / ± to zoom, tap a pin or a country
 * to see its photos. three.js, globe.gl and the country shapes (world-atlas, served from /geo) load
 * only when this view opens, so Hoje and the grid stay light.
 */
@Component({
  selector: 'app-photos-globe',
  imports: [Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div #host class="globe" [class.ready]="ready()" [class.far]="far()"></div>
    @if (!ready() && !failed()) {
      <p class="status">Carregando o globo…</p>
    }
    @if (failed()) {
      <p class="status">Não foi possível mostrar o globo neste aparelho.</p>
    }
    <div class="zoom">
      <button type="button" (click)="zoom(0.6)" aria-label="Aproximar"><app-icon name="plus" [size]="18" /></button>
      <button type="button" (click)="zoom(1 / 0.6)" aria-label="Afastar"><app-icon name="minus" [size]="18" /></button>
      <button type="button" (click)="overview()" aria-label="Ver o mundo todo"><app-icon name="globe" [size]="18" /></button>
    </div>
  `,
  styles: `
    :host { position: relative; display: block; height: min(70vh, 640px); min-height: 360px; border-radius: var(--radius); overflow: hidden; background: radial-gradient(circle at 50% 45%, color-mix(in srgb, var(--sky) 12%, var(--surface)), var(--surface) 70%); border: 1px solid var(--border); }
    .globe { position: absolute; inset: 0; opacity: 0; transition: opacity 0.6s; cursor: grab; touch-action: none; }
    .globe.ready { opacity: 1; }
    .status { position: absolute; inset: 0; display: grid; place-items: center; margin: 0; color: var(--text-faint); font-size: 0.9rem; }
    .zoom { position: absolute; right: 12px; bottom: 12px; display: flex; flex-direction: column; gap: 6px; }
    .zoom button { display: grid; place-items: center; width: 40px; height: 40px; border: 1px solid var(--border); border-radius: 10px; color: var(--text); background: var(--surface); box-shadow: var(--shadow); cursor: pointer; }
    .zoom button:hover { border-color: var(--sky); }
    @media (max-width: 560px) { :host { height: 60vh; } }

    /* Pins are created by globe.gl outside Angular's templates, hence ::ng-deep. */
    :host ::ng-deep .globe-pin {
      position: relative; display: flex; flex-direction: column; align-items: center; gap: 3px;
      padding: 0; border: 0; background: none; cursor: pointer;
      img { width: 40px; height: 40px; border: 2px solid #fff; border-radius: 50%; object-fit: cover; box-shadow: 0 2px 8px rgb(0 0 0 / 35%); transition: transform 0.2s; }
      .n { position: absolute; top: -4px; right: -6px; min-width: 18px; height: 18px; padding: 0 5px; border-radius: 9px; font-size: 0.68rem; font-weight: 700; line-height: 18px; color: #fff; background: var(--sky); }
      .name { padding: 1px 7px; border-radius: 6px; font-size: 0.72rem; font-weight: 600; white-space: nowrap; color: var(--text); background: color-mix(in srgb, var(--surface) 85%, transparent); }
      &:hover img, &.on img { transform: scale(1.18); border-color: var(--sky); }
    }
    /* From far away neighbouring pins overlap, so names show once you zoom in (or on the picked pin). */
    :host ::ng-deep .far .globe-pin:not(.on):not(:hover) .name { display: none; }
  `,
})
export class PhotosGlobe {
  readonly places = input.required<PlaceSummary[]>();
  /** The pin to highlight and fly to, if one is picked. */
  readonly selected = input<PlacePick | null>(null);
  readonly pick = output<PlacePick>();

  private readonly host = viewChild.required<ElementRef<HTMLElement>>('host');
  private readonly theme = inject(Theme);
  protected readonly ready = signal(false);
  protected readonly failed = signal(false);
  protected readonly far = signal(true);

  private globe?: GlobeInstance;
  private countries: Country[] = [];
  private contains?: (f: Country, point: [number, number]) => boolean;

  constructor() {
    const destroyRef = inject(DestroyRef);
    afterNextRender(() => this.init(destroyRef));

    // New pins (upload, edit) and theme switches restyle the globe without rebuilding it.
    effect(() => {
      const places = this.places();
      this.selected();
      this.theme.mode();
      if (this.ready()) this.draw(places);
    });

    effect(() => {
      const s = this.selected();
      const p = s && this.places().find((x) => samePlace(x, s));
      if (p && this.ready()) this.globe?.pointOfView({ lat: p.latitude, lng: p.longitude, altitude: NEAR }, 1200);
    });
  }

  protected zoom(factor: number) {
    if (!this.globe) return;
    const pov = this.globe.pointOfView();
    this.globe.pointOfView({ ...pov, altitude: Math.min(4, Math.max(0.08, pov.altitude * factor)) }, 400);
  }

  protected overview() {
    const pov = this.globe?.pointOfView();
    if (pov) this.globe!.pointOfView({ ...pov, altitude: FAR }, 800);
  }

  private async init(destroyRef: DestroyRef) {
    try {
      const [{ default: Globe }, topojson, d3, world] = await Promise.all([
        import('globe.gl'),
        import('topojson-client'),
        import('d3-geo'),
        fetch('/geo/countries-50m.json').then((r) => r.json()),
      ]);
      if (destroyRef.destroyed) return;

      this.contains = (f, point) => d3.geoContains(f, point);
      this.countries = (topojson.feature(world, world.objects.countries) as unknown as FeatureCollection<Geometry, { name: string }>).features;

      const el = this.host().nativeElement;
      const globe = new Globe(el, { rendererConfig: { antialias: true, alpha: true } })
        .width(el.clientWidth)
        .height(el.clientHeight)
        .backgroundColor('rgba(0,0,0,0)')
        .showGraticules(true)
        .polygonsData(this.countries)
        .polygonsTransitionDuration(300)
        .polygonLabel((d) => (d as Country).properties.name)
        .onPolygonClick((d) => this.pickCountry(d as Country))
        .onZoom((pov) => this.far.set(pov.altitude > 1.2))
        .htmlElementsData([])
        .htmlLat((d) => (d as PlaceSummary).latitude)
        .htmlLng((d) => (d as PlaceSummary).longitude)
        .htmlAltitude(0.015)
        .htmlTransitionDuration(0)
        .htmlElement((d) => this.pin(d as PlaceSummary));
      this.globe = globe;

      // Deep zoom down to a region; the slow spin stops for good once someone touches the globe.
      const controls = globe.controls();
      controls.minDistance = 108;
      controls.maxDistance = 600;
      controls.autoRotate = true;
      controls.autoRotateSpeed = 0.35;
      controls.addEventListener('start', () => (controls.autoRotate = false));

      const first = this.places()[0];
      globe.pointOfView({ lat: first?.latitude ?? 30, lng: first?.longitude ?? -10, altitude: FAR });

      const resize = new ResizeObserver(() => globe.width(el.clientWidth).height(el.clientHeight));
      resize.observe(el);
      const dark = matchMedia('(prefers-color-scheme: dark)');
      const onScheme = () => this.draw(this.places());
      dark.addEventListener('change', onScheme);
      destroyRef.onDestroy(() => {
        resize.disconnect();
        dark.removeEventListener('change', onScheme);
        globe._destructor();
      });

      this.ready.set(true);
    } catch (e) {
      console.error('Globe failed to load', e);
      this.failed.set(true);
    }
  }

  /** Colours come from the theme tokens, so the globe follows light/dark like the rest of the page. */
  private draw(places: PlaceSummary[]) {
    const globe = this.globe;
    if (!globe) return;
    const css = getComputedStyle(document.documentElement);
    const token = (name: string) => css.getPropertyValue(name).trim();
    const sky = token('--sky');
    const land = token('--surface-2');
    const border = token('--border-strong');
    const ocean = blend(token('--bg'), sky, 0.18);

    for (const c of this.countries) c.visited = places.some((p) => this.contains!(c, [p.longitude, p.latitude]));

    const material = globe.globeMaterial() as unknown as { color: { set(c: string): void } };
    material.color.set(ocean);
    globe
      .atmosphereColor(sky)
      .polygonCapColor((d) => ((d as Country).visited ? mix(sky, 0.55) : land))
      .polygonSideColor(() => mix(border, 0.4))
      .polygonStrokeColor(() => border)
      .polygonAltitude((d) => ((d as Country).visited ? 0.012 : 0.005))
      // Fresh objects so every pin is rebuilt and picks up the selection.
      .htmlElementsData(places.map((p) => ({ ...p })));
  }

  private pin(p: PlaceSummary): HTMLElement {
    const s = this.selected();
    const el = document.createElement('button');
    el.type = 'button';
    el.className = 'globe-pin' + (s && samePlace(p, s) ? ' on' : '');
    el.title = `${placeLabel(p)} · ${p.count} ${p.count === 1 ? 'foto' : 'fotos'}`;
    el.setAttribute('aria-label', el.title);
    el.style.pointerEvents = 'auto';

    const img = document.createElement('img');
    img.src = p.coverUrl;
    img.alt = '';
    const count = document.createElement('span');
    count.className = 'n';
    count.textContent = String(p.count);
    const name = document.createElement('span');
    name.className = 'name';
    name.textContent = p.city ?? p.country;
    el.append(img, count, name);

    el.addEventListener('click', (e) => {
      e.stopPropagation();
      this.pick.emit({ country: p.country, city: p.city });
    });
    return el;
  }

  /** A country with photos opens all of them; one without just turns the globe towards it. */
  private pickCountry(c: Country) {
    const inside = this.places().filter((p) => this.contains!(c, [p.longitude, p.latitude]));
    if (inside.length) this.pick.emit({ country: inside[0].country, city: null });
  }
}

export function samePlace(a: { country: string | null; city: string | null }, b: PlacePick): boolean {
  return fold(a.country) === fold(b.country) && (b.city === null || fold(a.city) === fold(b.city));
}

export function placeLabel(p: { country: string | null; city: string | null }): string {
  return [p.city, p.country].filter(Boolean).join(', ');
}

/** Case- and accent-insensitive, like the API: "sao paulo" is "São Paulo". */
function fold(s: string | null): string {
  return (s ?? '').normalize('NFD').replace(/\p{Mn}/gu, '').trim().toLocaleLowerCase();
}

/** three.js can't read color-mix(), so theme hex tokens are mixed here. */
function rgb(hex: string): [number, number, number] | null {
  const m = /^#([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i.exec(hex);
  return m ? [parseInt(m[1], 16), parseInt(m[2], 16), parseInt(m[3], 16)] : null;
}

function mix(hex: string, alpha: number): string {
  const c = rgb(hex);
  return c ? `rgba(${c.join(', ')}, ${alpha})` : hex;
}

/** `a` with a share `t` of `b`, e.g. the page background tinted blue for the oceans. */
function blend(a: string, b: string, t: number): string {
  const x = rgb(a);
  const y = rgb(b);
  if (!x || !y) return a;
  return `rgb(${x.map((v, i) => Math.round(v + (y[i] - v) * t)).join(', ')})`;
}
