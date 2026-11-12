import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { Router } from '@angular/router';
import { Clock, fmt } from '../core/clock';
import { DashboardData } from '../core/dashboard-data';
import { Accent, Exam, ExamKind, ExamOutcome, ExamResult, HealthData, Marker, SaveExam } from '../core/models';
import {
  EXAM_KINDS, Health, MarkerReading, MarkerStatus, STATUS_LABEL, addDays, checkupDue, examKindOf, kg, markerStatus, num, rangeLabel,
  readingsFor, signedKg, weekAverage, weightsFor, worst,
} from '../core/health';
import { valueOf } from '../shared/resource-state';
import { Icon } from '../shared/icon';

interface Person { id: string; name: string; color: Accent }

/** One editable line of the exam form; values stay strings until saving so "5,6" can be typed. */
interface ResultDraft { key: number; marker: string; value: string; refLow: string; refHigh: string }

/** Markers added by "Adicionar painel básico", per exam kind. */
const BASIC_PANELS: Partial<Record<ExamKind, string[]>> = {
  sangue: ['glicose', 'hba1c', 'colesterol', 'ldl', 'hdl', 'triglicerideos', 'tgo', 'tgp', 'creatinina', 'tsh', 'hemoglobina', 'vitamina-d', 'ferritina'],
  medidas: ['pressao-sistolica', 'pressao-diastolica', 'frequencia-cardiaca'],
};

/** A weigh-in this far (kg) from the median of the same person's readings within a week is flagged. */
const ODD_WEIGHT_KG = 3;

const RANGES = [
  { days: 30, label: '30 dias' },
  { days: 90, label: '90 dias' },
  { days: 365, label: '1 ano' },
  { days: 0, label: 'Tudo' },
];

const TONE_HEX: Record<Accent, string> = {
  sky: 'var(--sky)', amber: 'var(--amber)', green: 'var(--green)', violet: 'var(--violet)', rose: 'var(--rose)', teal: 'var(--teal)',
};

// Chart canvas, in SVG user units. The right gutter holds the latest-value and goal labels.
const W = 640;
const PAD = { top: 18, right: 66, bottom: 26, left: 40 };
/** Panel height: one tall chart for a single person, shorter stacked panels for several. */
const chartHeight = (panels: number) => (panels > 1 ? 172 : 230);

/**
 * Saúde: weight progress toward each person's goal, the status of every exam marker by area (as of
 * any date), what needs attention, the exam history and recurring checkups. ?pessoa=leesy shows one
 * person, so each tablet can be left on its owner.
 */
@Component({
  selector: 'app-health-page',
  imports: [Icon, NgTemplateOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './health-page.html',
  styleUrl: './health-page.scss',
})
export class HealthPage {
  /** ?pessoa=<person id>; empty shows everyone. */
  readonly pessoa = input<string>();

  private readonly health = inject(Health);
  private readonly clock = inject(Clock);
  private readonly router = inject(Router);
  private readonly config = inject(DashboardData).config;

  /** Exams, checkups and marker statuses are hidden for now: the tab only tracks weight. Flip to bring them back. */
  protected readonly showExams = false;
  protected readonly examKinds = EXAM_KINDS;
  protected readonly ranges = RANGES;
  protected readonly statusLabel = STATUS_LABEL;
  protected readonly fmt = fmt;
  protected readonly kg = kg;
  protected readonly num = num;
  protected readonly rangeLabel = rangeLabel;
  protected readonly kindOf = examKindOf;
  protected readonly error = this.health.error;
  protected readonly loading = computed(() => !this.health.res.hasValue() && !this.health.res.error());

  protected readonly people = computed<Person[]>(() => valueOf(this.config)?.people ?? []);
  protected readonly person = computed(() => this.people().find((p) => p.id === this.pessoa()) ?? null);
  protected readonly shown = computed(() => (this.person() ? [this.person()!] : this.people()));
  protected readonly defaultPerson = computed(() => this.person()?.id ?? this.people()[0]?.id ?? '');

  protected readonly today = computed(() => fmt.dayKey(this.clock.now()));
  /** "Ver como estava em": null means today. */
  protected readonly asOfPick = signal<string | null>(null);
  protected readonly asOf = computed(() => this.asOfPick() ?? this.today());

  protected readonly range = signal(90);
  protected readonly showEntries = signal(false);
  protected readonly editingGoals = signal<string | null>(null);
  protected readonly openArea = signal<string | null>(null);
  protected readonly openExam = signal<string | null>(null);
  protected readonly showAllExams = signal(false);
  protected readonly addingCheckup = signal(false);

  /** 'new', an exam id being edited, or null. */
  protected readonly editing = signal<string | null>(null);
  protected readonly draftKind = signal<ExamKind>('sangue');
  protected readonly drafts = signal<ResultDraft[]>([]);
  private nextKey = 0;

  private readonly data = computed(() => this.health.data());

  protected readonly markersByArea = computed(() => {
    const groups = new Map<string, Marker[]>();
    for (const m of this.data()?.markers ?? []) groups.set(m.area, [...(groups.get(m.area) ?? []), m]);
    return [...groups].map(([area, markers]) => ({ area, markers }));
  });
  private readonly markerMap = computed(() => new Map((this.data()?.markers ?? []).map((m) => [m.code, m])));

  // ---------- Weight ----------

  protected readonly weightCards = computed(() => {
    const data = this.data();
    if (!data) return [];
    const asOf = this.asOf();
    return this.shown().map((p) => {
      const profile = data.profiles.find((x) => x.personId === p.id);
      const all = weightsFor(data.weights, p.id, '0000-01-01', asOf);
      const latest = all.at(-1) ?? null;
      const avg = weekAverage(data.weights, p.id, asOf);
      const prevAvg = weekAverage(data.weights, p.id, addDays(asOf, -7));
      const start = profile?.startWeightKg ?? all[0]?.kg ?? null;
      const goal = profile?.goalWeightKg ?? null;
      const current = latest?.kg ?? null;
      const pct = start !== null && goal !== null && current !== null && start !== goal
        ? Math.max(0, Math.min(100, Math.round(((start - current) / (start - goal)) * 100)))
        : null;
      const height = profile?.heightCm ?? null;
      return {
        ...p,
        profile,
        latest,
        latestWhen: latest ? (latest.date === this.today() ? 'hoje' : fmt.dateOnlyShort(latest.date)) : null,
        sinceStart: start !== null && current !== null ? signedKg(current - start) : null,
        lost: start !== null && current !== null && current < start,
        goal,
        toGo: goal !== null && current !== null ? Math.max(0, current - goal) : null,
        pct,
        week: avg !== null && prevAvg !== null ? signedKg(avg - prevAvg) : null,
        weekDown: avg !== null && prevAvg !== null && avg < prevAvg,
        bmi: height && current ? (current / (height / 100) ** 2).toFixed(1).replace('.', ',') : null,
        streak: streak(data, p.id, asOf),
      };
    });
  });

  /** First day of the chart period; the entries list below the chart covers the same days. */
  private readonly chartFrom = computed(() => {
    const to = this.asOf();
    const ids = new Set(this.shown().map((p) => p.id));
    const earliest = (this.data()?.weights ?? []).find((w) => ids.has(w.personId))?.date ?? to;
    return this.range() ? addDays(to, -this.range()) : earliest;
  });

  /** Weigh-ins far from the person's other readings that week: usually a typo or the wrong person. */
  private readonly oddWeights = computed(() => {
    const weights = this.data()?.weights ?? [];
    const odd = new Set<string>();
    for (const w of weights) {
      const near = weightsFor(weights, w.personId, addDays(w.date, -7), addDays(w.date, 7)).filter((x) => x.id !== w.id);
      if (near.length >= 2 && Math.abs(w.kg - median(near.map((x) => x.kg))) > ODD_WEIGHT_KG) odd.add(w.id);
    }
    return odd;
  });

  /**
   * One panel per person, sharing the time axis but each with its own weight scale: two people's
   * weights are far apart, and a shared scale flattens both lines.
   */
  protected readonly chart = computed(() => {
    const data = this.data();
    if (!data) return null;
    const to = this.asOf();
    const from = this.chartFrom();

    const series = this.shown().map((p) => {
      const points = weightsFor(data.weights, p.id, from, to);
      const goal = data.profiles.find((x) => x.personId === p.id)?.goalWeightKg ?? null;
      return { person: p, color: TONE_HEX[p.color] ?? 'var(--accent)', points, goal };
    }).filter((s) => s.points.length);
    if (!series.length) return null;

    // Start at the first weigh-in in the period, so a short history isn't squeezed into one corner.
    let start = series.reduce((d, s) => (s.points[0].date < d ? s.points[0].date : d), to);
    if (dayDiff(start, to) < 6) start = addDays(to, -6);
    const span = dayDiff(start, to);
    const h = chartHeight(series.length);
    const top = PAD.top, bottom = h - PAD.bottom, left = PAD.left, right = W - PAD.right;
    const x = (d: string) => left + (dayDiff(start, d) / span) * (right - left);
    const xTicks = dateTicks(start, to).map((t) => ({ ...t, x: x(t.day) })).filter((t) => t.x - left > 18 || t.x === left);
    const odd = this.oddWeights();
    const many = Math.max(...series.map((s) => s.points.length)) > 60;

    const panels = series.map((s) => {
      const kgs = s.points.map((w) => w.kg);
      // 7-day average: smoother than the daily readings, which swing with water and salt.
      const avgs = s.points.map((w) => weekAverage(data.weights, s.person.id, w.date)!);
      let lo = Math.min(...kgs, ...avgs), hi = Math.max(...kgs, ...avgs);
      // Pull the goal into the scale only when it's close; a far-away goal would flatten the line,
      // so it gets an arrow label at the edge instead.
      const near = s.goal !== null && s.goal >= lo - Math.max(2, hi - lo) && s.goal <= hi + Math.max(2, hi - lo);
      if (near) { lo = Math.min(lo, s.goal!); hi = Math.max(hi, s.goal!); }
      const scale = niceScale(lo, hi);
      const y = (v: number) => top + ((scale.hi - v) / (scale.hi - scale.lo)) * (bottom - top);

      const dots = s.points.map((w, i) => ({
        id: w.id, date: w.date, kg: w.kg, avg: avgs[i], cx: x(w.date), cy: y(w.kg), ay: y(avgs[i]), odd: odd.has(w.id),
      }));
      const last = dots[dots.length - 1];
      const line = dots.length >= 3 ? dots.map((d, i) => `${i ? 'L' : 'M'}${d.cx.toFixed(1)},${d.ay.toFixed(1)}`).join(' ') : null;

      // Min/max labels, skipped when they'd sit on the latest point (it has its own label).
      const extreme = (pick: (a: number, b: number) => boolean, above: boolean) => {
        const i = kgs.reduce((best, v, j) => (pick(v, kgs[best]) ? j : best), 0);
        const d = dots[i];
        if (dots.length < 3 || i === dots.length - 1 || Math.abs(d.cx - last.cx) < 30) return null;
        const anchor = d.cx < left + 40 ? 'start' : d.cx > right - 40 ? 'end' : 'middle';
        return { tx: anchor === 'start' ? d.cx - 4 : anchor === 'end' ? d.cx + 4 : d.cx, ty: above ? d.cy - 8 : d.cy + 15, anchor, label: `${above ? 'máx' : 'mín'} ${kg(d.kg)}` };
      };

      let endY = clamp(last.cy + 4, top + 4, bottom);
      let goal: { y: number | null; ty: number; label: string } | null = null;
      if (s.goal !== null) {
        const gy = near ? y(s.goal) : null;
        let ty = gy !== null ? gy + 4 : s.goal < scale.lo ? bottom - 2 : top + 8;
        // Keep the goal label from overlapping the latest-value label in the gutter.
        if (Math.abs(ty - endY) < 13) {
          if (ty >= endY) ty = Math.min(bottom + 8, endY + 13);
          else ty = Math.max(top - 4, endY - 13);
          if (Math.abs(ty - endY) < 13) endY = ty >= endY ? ty - 13 : ty + 13;
        }
        goal = { y: gy, ty, label: gy !== null ? `meta ${num(s.goal)}` : `${s.goal < scale.lo ? '↓' : '↑'} meta ${num(s.goal)}` };
      }

      const days = dayDiff(s.points[0].date, last.date);
      const change = days >= 3 ? avgs[avgs.length - 1] - avgs[0] : null;
      return {
        person: s.person,
        color: s.color,
        yTicks: scale.ticks.map((v) => ({ y: y(v), label: scale.step < 1 ? kg(v) : num(v) })),
        dots,
        line,
        area: line ? `${line} L${last.cx.toFixed(1)},${bottom} L${dots[0].cx.toFixed(1)},${bottom} Z` : null,
        max: extreme((a, b) => a > b, true),
        min: extreme((a, b) => a < b, false),
        last,
        end: { y: endY, label: kg(last.kg) },
        goal,
        change: change !== null ? `${signedKg(change)} kg` : null,
        changeDown: change !== null && change < 0,
        changeSpan: change !== null ? `em ${ago(days)}` : null,
      };
    });

    return { w: W, h, top, bottom, left, right, xTicks, panels, many, gradient: `wg-${this.uid}` };
  });

  /** Unique id prefix for SVG gradients, in case the page is ever rendered twice. */
  private readonly uid = Math.random().toString(36).slice(2, 8);

  /** The point under the finger or mouse, per panel: hover on desktop, tap on the tablets. */
  protected readonly hover = signal<{ personId: string; id: string } | null>(null);
  protected readonly tip = computed(() => {
    const h = this.hover();
    const ch = this.chart();
    const panel = h && ch?.panels.find((p) => p.person.id === h.personId);
    const d = panel?.dots.find((x) => x.id === h!.id);
    if (!ch || !panel || !d) return null;
    const xPct = (d.cx / ch.w) * 100;
    // Above the higher of the two marks, or below the lower one when there's no room above.
    const below = Math.min(d.cy, d.ay) < ch.h * 0.38;
    return {
      personId: panel.person.id,
      dot: d,
      color: panel.color,
      left: xPct,
      top: ((below ? Math.max(d.cy, d.ay) : Math.min(d.cy, d.ay)) / ch.h) * 100,
      align: xPct < 22 ? 'start' : xPct > 70 ? 'end' : 'middle',
      below,
      when: `${fmt.dateOnlyShort(d.date)} ${d.date.slice(0, 4)}`,
      kg: kg(d.kg),
      avg: kg(d.avg),
    };
  });

  protected pointAt(ev: PointerEvent, personId: string) {
    const ch = this.chart();
    const panel = ch?.panels.find((p) => p.person.id === personId);
    if (!ch || !panel) return;
    const box = (ev.currentTarget as Element).getBoundingClientRect();
    const ux = ((ev.clientX - box.left) / box.width) * ch.w;
    const near = panel.dots.reduce((best, d) => (Math.abs(d.cx - ux) < Math.abs(best.cx - ux) ? d : best));
    const cur = this.hover();
    if (cur?.personId !== personId || cur.id !== near.id) this.hover.set({ personId, id: near.id });
  }

  protected leaveChart(ev: PointerEvent) {
    // On touch the tooltip stays until the next tap, so its button can be pressed.
    if (ev.pointerType === 'mouse') this.hover.set(null);
  }

  /** Every weigh-in in the chart period, newest first; odd-looking ones are flagged so they're easy to spot. */
  protected readonly entries = computed(() => {
    const data = this.data();
    if (!data) return [];
    const ids = new Set(this.shown().map((p) => p.id));
    const from = this.chartFrom();
    return data.weights
      .filter((w) => ids.has(w.personId) && w.date >= from && w.date <= this.asOf())
      .reverse()
      .map((w) => ({
        ...w,
        who: this.personOf(w.personId),
        when: `${fmt.dateOnlyShort(w.date)} ${w.date.slice(0, 4)}`,
        odd: this.oddWeights().has(w.id),
      }));
  });
  protected readonly oddCount = computed(() => this.entries().filter((w) => w.odd).length);
  /** The entry picked from the chart, highlighted in the list. */
  protected readonly focusedEntry = signal<string | null>(null);

  // ---------- Exams: status by area ----------

  private readonly readings = computed(() => {
    const data = this.data();
    if (!data) return [];
    return this.shown().map((p) => ({ person: p, byMarker: readingsFor(data, p.id, this.asOf()) }));
  });

  protected readonly areas = computed(() => {
    const readings = this.readings();
    return this.markersByArea()
      .map(({ area, markers }) => {
        const perPerson = readings.map(({ person, byMarker }) => {
          const list = markers.map((m) => byMarker.get(m.code)).filter((r): r is MarkerReading => !!r);
          return { person, status: worst(list.map((r) => r.latest.status)), count: list.length };
        });
        const rows = markers.flatMap((m) =>
          readings
            .map(({ person, byMarker }) => byMarker.get(m.code) && this.readingView(byMarker.get(m.code)!, person))
            .filter((r) => !!r),
        );
        return { area, perPerson, rows, markerNames: markers.map((m) => m.name).slice(0, 3).join(', ') };
      })
      .filter((a) => a.rows.length);
  });

  protected readonly attention = computed(() =>
    this.readings()
      .flatMap(({ person, byMarker }) => [...byMarker.values()].filter((r) => r.latest.status !== 'ok').map((r) => this.readingView(r, person)))
      .sort((a, b) => (a.status === b.status ? a.name.localeCompare(b.name) : a.status === 'alterado' ? -1 : 1)),
  );

  private readingView(r: MarkerReading, person: Person) {
    return {
      key: `${person.id}:${r.marker.code}`,
      name: r.marker.name,
      who: person,
      value: `${num(r.latest.value)} ${r.marker.unit}`,
      range: `${rangeLabel(r.latest.low, r.latest.high)} ${r.marker.unit}`,
      status: r.latest.status,
      when: fmt.dateOnlyShort(r.latest.date),
      previous: r.history.length > 1 ? num(r.history[r.history.length - 2].value) : null,
      spark: sparkline(r.history.map((h) => h.value), r.latest.low, r.latest.high),
    };
  }

  // ---------- Exams: history ----------

  protected readonly exams = computed(() => {
    const data = this.data();
    if (!data) return [];
    const ids = new Set(this.shown().map((p) => p.id));
    const list = data.exams.filter((e) => ids.has(e.personId) && e.date <= this.asOf());
    return (this.showAllExams() ? list : list.slice(0, 8)).map((e) => this.examView(e));
  });
  protected readonly examCount = computed(() => {
    const ids = new Set(this.shown().map((p) => p.id));
    return (this.data()?.exams ?? []).filter((e) => ids.has(e.personId) && e.date <= this.asOf()).length;
  });

  private examView(e: Exam) {
    const results = e.results.map((r) => {
      const m = this.markerMap().get(r.marker);
      const low = r.refLow ?? m?.refLow ?? null;
      const high = r.refHigh ?? m?.refHigh ?? null;
      const status = markerStatus(r.value, low, high);
      return { name: m?.name ?? r.marker, value: `${num(r.value)} ${m?.unit ?? ''}`, range: rangeLabel(low, high), status };
    });
    const off = results.filter((r) => r.status !== 'ok').length;
    const summary = results.length
      ? `${results.length} ${results.length === 1 ? 'resultado' : 'resultados'}${off ? ` · ${off} fora da referência` : ''}`
      : e.outcome === 'alterado' ? 'alterado' : e.outcome === 'normal' ? 'normal' : null;
    return {
      ...e,
      when: fmt.dateOnlyShort(e.date),
      year: e.date.slice(0, 4),
      who: this.personOf(e.personId),
      icon: examKindOf(e.kind).icon,
      summary,
      flagged: off > 0 || e.outcome === 'alterado',
      resultViews: results,
    };
  }

  protected readonly editingExam = computed(() => this.data()?.exams.find((e) => e.id === this.editing()) ?? null);
  protected readonly titleSuggestions = computed(() =>
    [...new Set([...(this.data()?.exams ?? []).map((e) => e.title), ...(this.data()?.checkups ?? []).map((c) => c.name)])].sort(),
  );

  // ---------- Checkups ----------

  protected readonly checkups = computed(() => {
    const data = this.data();
    if (!data) return [];
    const ids = new Set(this.shown().map((p) => p.id));
    return data.checkups
      .filter((c) => ids.has(c.personId))
      .map((c) => {
        const due = checkupDue(c, this.today());
        const tone: MarkerStatus = due.days < 0 || !c.lastDone ? 'alterado' : due.days <= 30 ? 'atencao' : 'ok';
        const label = !c.lastDone ? 'nunca feito'
          : due.days < 0 ? `atrasado ${ago(-due.days)}`
          : due.days === 0 ? 'vence hoje'
          : `em ${ago(due.days)}`;
        return {
          ...c,
          who: this.personOf(c.personId),
          icon: examKindOf(c.kind).icon,
          days: due.days,
          tone,
          label,
          last: c.lastDone ? `última vez ${fmt.dateOnlyShort(c.lastDone)} ${c.lastDone.slice(0, 4)}` : null,
          every: c.everyMonths === 12 ? 'todo ano' : c.everyMonths % 12 === 0 ? `a cada ${c.everyMonths / 12} anos` : `a cada ${c.everyMonths} meses`,
        };
      })
      .sort((a, b) => a.days - b.days);
  });

  // ---------- Actions ----------

  protected showPerson(id: string | null) {
    this.router.navigate([], { queryParams: { pessoa: id }, replaceUrl: true });
  }

  protected pickAsOf(value: string) {
    this.asOfPick.set(value && value !== this.today() ? value : null);
  }

  protected async saveWeight(event: Event, form: HTMLFormElement) {
    event.preventDefault();
    const data = new FormData(form);
    const kgValue = parseNum(String(data.get('kg') ?? ''));
    const personId = String(data.get('personId') ?? '');
    if (kgValue === null || !personId) return;
    const ok = await this.health.saveWeight({ personId, date: String(data.get('date') || this.today()), kg: kgValue, note: null });
    if (ok) (form.querySelector('[name=kg]') as HTMLInputElement).value = '';
  }

  protected removeWeight(w: { id: string; when: string; kg: number; who: { name: string } }) {
    if (confirm(`Excluir o peso de ${w.who.name} em ${w.when} (${kg(w.kg)} kg)?`)) this.health.removeWeight(w.id);
  }

  /** Tapping a point on the chart opens the list at that weigh-in, so a wrong one is easy to find and delete. */
  protected focusEntry(id: string) {
    this.showEntries.set(true);
    this.focusedEntry.set(id);
    setTimeout(() => document.getElementById(`weight-${id}`)?.scrollIntoView({ behavior: 'smooth', block: 'center' }));
  }

  protected async saveGoals(event: Event, form: HTMLFormElement, personId: string) {
    event.preventDefault();
    const data = new FormData(form);
    const n = (k: string) => parseNum(String(data.get(k) ?? ''));
    const ok = await this.health.saveProfile(personId, { heightCm: n('heightCm'), startWeightKg: n('startWeightKg'), goalWeightKg: n('goalWeightKg') });
    if (ok) this.editingGoals.set(null);
  }

  protected startExam(exam: Exam | null) {
    this.editing.set(exam?.id ?? 'new');
    this.draftKind.set(exam?.kind ?? 'sangue');
    this.drafts.set((exam?.results ?? []).map((r) => ({
      key: this.nextKey++,
      marker: r.marker,
      value: String(r.value).replace('.', ','),
      refLow: r.refLow === null ? '' : String(r.refLow).replace('.', ','),
      refHigh: r.refHigh === null ? '' : String(r.refHigh).replace('.', ','),
    })));
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  protected addDraft(marker = '') {
    this.drafts.update((d) => [...d, { key: this.nextKey++, marker, value: '', refLow: '', refHigh: '' }]);
  }

  /** Adds the usual markers for this kind of exam that aren't in the form yet. */
  protected addBasicPanel() {
    const have = new Set(this.drafts().map((d) => d.marker));
    for (const code of BASIC_PANELS[this.draftKind()] ?? BASIC_PANELS.sangue!) if (!have.has(code)) this.addDraft(code);
  }

  protected hasBasicPanel = computed(() => !!BASIC_PANELS[this.draftKind()]);

  protected setDraft(key: number, field: 'marker' | 'value' | 'refLow' | 'refHigh', value: string) {
    this.drafts.update((list) => list.map((d) => (d.key === key ? { ...d, [field]: value } : d)));
  }

  protected removeDraft(key: number) {
    this.drafts.update((list) => list.filter((d) => d.key !== key));
  }

  protected draftMarker(code: string) {
    return this.markerMap().get(code) ?? null;
  }

  protected async saveExam(event: Event, form: HTMLFormElement) {
    event.preventDefault();
    const data = new FormData(form);
    const text = (k: string) => String(data.get(k) ?? '').trim() || null;
    const results: ExamResult[] = this.drafts()
      .filter((d) => d.marker && parseNum(d.value) !== null)
      .map((d) => ({ marker: d.marker, value: parseNum(d.value)!, refLow: parseNum(d.refLow), refHigh: parseNum(d.refHigh) }));
    const exam: SaveExam = {
      personId: text('personId') ?? '',
      date: text('date') ?? this.today(),
      kind: this.draftKind(),
      title: text('title') ?? '',
      provider: text('provider'),
      outcome: (text('outcome') as ExamOutcome | null) ?? null,
      notes: text('notes'),
      results,
    };
    if (!exam.title || !exam.personId) return;
    const id = this.editing();
    const ok = id && id !== 'new' ? await this.health.updateExam(id, exam) : await this.health.createExam(exam);
    if (ok) this.editing.set(null);
  }

  protected removeExam(e: { id: string; title: string; when: string }) {
    if (confirm(`Excluir "${e.title}" de ${e.when}?`)) {
      this.health.removeExam(e.id);
      this.editing.set(null);
    }
  }

  protected async saveCheckup(event: Event, form: HTMLFormElement) {
    event.preventDefault();
    const data = new FormData(form);
    const text = (k: string) => String(data.get(k) ?? '').trim() || null;
    const name = text('name');
    const personId = text('personId');
    if (!name || !personId) return;
    const ok = await this.health.createCheckup({
      personId,
      name,
      kind: (text('kind') as ExamKind | null) ?? 'consulta',
      everyMonths: Number(text('everyMonths') ?? 12),
      lastDone: text('lastDone'),
    });
    if (ok) this.addingCheckup.set(false);
  }

  protected markDone(id: string) {
    this.health.markCheckupDone(id, this.today());
  }

  protected removeCheckup(c: { id: string; name: string }) {
    if (confirm(`Parar de acompanhar "${c.name}"?`)) this.health.removeCheckup(c.id);
  }

  private personOf(id: string) {
    return this.people().find((p) => p.id === id) ?? { id, name: id, color: 'violet' as Accent };
  }
}

/** Accepts "88,4" as well as "88.4"; empty or invalid gives null. */
function parseNum(s: string): number | null {
  const n = Number(s.trim().replace(',', '.'));
  return s.trim() && Number.isFinite(n) ? n : null;
}

function median(values: number[]) {
  const sorted = [...values].sort((a, b) => a - b);
  const mid = Math.floor(sorted.length / 2);
  return sorted.length % 2 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
}

function dayDiff(from: string, to: string) {
  return Math.round((Date.parse(to) - Date.parse(from)) / 86_400_000);
}

const clamp = (v: number, lo: number, hi: number) => Math.min(hi, Math.max(lo, v));

/** Round axis bounds and 3–6 ticks on a 0,5 / 1 / 2 / 5 / 10 kg step, with a little headroom. */
function niceScale(min: number, max: number) {
  const pad = Math.max(0.3, (max - min) * 0.08);
  const range = Math.max(2, max - min + 2 * pad);
  const step = [0.5, 1, 2, 5, 10, 20].find((s) => range / s <= 5) ?? 50;
  const lo = Math.floor((min - pad) / step) * step;
  const hi = Math.ceil((max + pad) / step) * step;
  const ticks: number[] = [];
  for (let v = lo; v <= hi + 1e-9; v += step) ticks.push(Math.round(v * 10) / 10);
  return { lo, hi, step, ticks };
}

/** Date ticks that suit the span: every few days, weekly (Mondays), monthly or quarterly. */
function dateTicks(from: string, to: string): { day: string; label: string }[] {
  const span = dayDiff(from, to);
  const dayLabel = (d: string) => `${new Date(d).getUTCDate()} ${fmt.dateOnlyMonthShort(d)}`;
  const out: { day: string; label: string }[] = [];
  if (span <= 75) {
    let day = from;
    let step = Math.max(1, Math.ceil(span / 6));
    if (span > 14) {
      // Align to Mondays so the ticks read as weeks.
      while (new Date(day).getUTCDay() !== 1) day = addDays(day, 1);
      step = span > 45 ? 14 : 7;
    }
    for (; day <= to; day = addDays(day, step)) out.push({ day, label: dayLabel(day) });
    return out;
  }
  const months = span <= 200 ? 1 : span <= 400 ? 2 : span <= 800 ? 3 : 6;
  const d = new Date(from);
  d.setUTCDate(1);
  d.setUTCMonth(d.getUTCMonth() + 1);
  while (d.getUTCMonth() % months) d.setUTCMonth(d.getUTCMonth() + 1);
  for (; d.toISOString().slice(0, 10) <= to; d.setUTCMonth(d.getUTCMonth() + months)) {
    const day = d.toISOString().slice(0, 10);
    const label = fmt.dateOnlyMonthShort(day);
    out.push({ day, label: d.getUTCMonth() === 0 || span > 400 ? `${label} ${String(d.getUTCFullYear()).slice(2)}` : label });
  }
  return out;
}

/** "3 dias", "2 meses", "1 ano" */
function ago(days: number) {
  if (days < 45) return days === 1 ? '1 dia' : `${days} dias`;
  const months = Math.round(days / 30);
  if (months < 12) return months === 1 ? '1 mês' : `${months} meses`;
  const years = Math.round(days / 365);
  return years === 1 ? '1 ano' : `${years} anos`;
}

/** Days in a row with a weigh-in, ending on asOf (or the day before, if today's isn't in yet). */
function streak(data: HealthData, personId: string, asOf: string) {
  const days = new Set(data.weights.filter((w) => w.personId === personId).map((w) => w.date));
  let day = days.has(asOf) ? asOf : addDays(asOf, -1);
  let n = 0;
  while (days.has(day)) {
    n++;
    day = addDays(day, -1);
  }
  return n;
}

/** A tiny trend line (60×18) for a marker's readings, with the reference range shaded. */
function sparkline(values: number[], low: number | null, high: number | null) {
  const w = 60, h = 18;
  const all = [...values, ...(low !== null ? [low] : []), ...(high !== null ? [high] : [])];
  const lo = Math.min(...all), hi = Math.max(...all);
  const y = (v: number) => (hi === lo ? h / 2 : 2 + ((hi - v) / (hi - lo)) * (h - 4));
  const x = (i: number) => (values.length === 1 ? w / 2 : 2 + (i / (values.length - 1)) * (w - 4));
  const bandTop = y(high ?? hi), bandBottom = y(low ?? lo);
  return {
    points: values.map((v, i) => `${x(i).toFixed(1)},${y(v).toFixed(1)}`).join(' '),
    last: { cx: x(values.length - 1), cy: y(values[values.length - 1]) },
    band: { y: bandTop, height: Math.max(0, bandBottom - bandTop) },
  };
}
