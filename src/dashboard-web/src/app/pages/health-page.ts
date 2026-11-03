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

// Chart canvas, in SVG user units.
const W = 640;
const H = 200;
const PAD = { top: 12, right: 12, bottom: 24, left: 36 };

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

  protected readonly chart = computed(() => {
    const data = this.data();
    if (!data) return null;
    const to = this.asOf();
    const shown = this.shown();
    const from = this.chartFrom();
    const span = Math.max(1, dayDiff(from, to));

    const series = shown.map((p) => {
      const points = weightsFor(data.weights, p.id, from, to);
      const goal = data.profiles.find((x) => x.personId === p.id)?.goalWeightKg ?? null;
      return { person: p, color: TONE_HEX[p.color] ?? 'var(--accent)', points, goal };
    }).filter((s) => s.points.length);
    if (!series.length) return null;

    const values = series.flatMap((s) => [...s.points.map((w) => w.kg), ...(s.goal !== null ? [s.goal] : [])]);
    const lo = Math.floor(Math.min(...values) - 1);
    const hi = Math.ceil(Math.max(...values) + 1);
    const x = (d: string) => PAD.left + (dayDiff(from, d) / span) * (W - PAD.left - PAD.right);
    const y = (v: number) => PAD.top + ((hi - v) / (hi - lo)) * (H - PAD.top - PAD.bottom);

    const ticks = [hi, Math.round((hi + lo) / 2), lo].map((v) => ({ y: y(v), label: String(v) }));
    const dates = [from, addDays(from, Math.round(span / 2)), to].map((d) => ({ x: x(d), label: `${new Date(d).getUTCDate()} ${fmt.dateOnlyMonthShort(d)}` }));

    return {
      w: W, h: H, ticks, dates, left: PAD.left, right: W - PAD.right,
      series: series.map((s) => ({
        ...s,
        dots: s.points.map((w) => ({
          id: w.id,
          cx: x(w.date),
          cy: y(w.kg),
          odd: this.oddWeights().has(w.id),
          title: `${s.person.name} · ${fmt.dateOnlyShort(w.date)} · ${kg(w.kg)} kg`,
        })),
        // 7-day average line: smoother than the daily readings, which swing with water and salt.
        trend: s.points.map((w) => `${x(w.date).toFixed(1)},${y(weekAverage(data.weights, s.person.id, w.date)!).toFixed(1)}`).join(' '),
        goalY: s.goal !== null ? y(s.goal) : null,
      })),
    };
  });

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
