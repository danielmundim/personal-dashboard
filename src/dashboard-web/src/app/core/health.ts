import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { MINUTE } from './dashboard-data';
import { Checkup, ExamKind, HealthData, Marker, SaveCheckup, SaveExam, SaveProfile, SaveWeight, WeightEntry } from './models';
import { pollingResource } from './polling-resource';
import { valueOf } from '../shared/resource-state';
import { saveErrorMessage } from './save-error';
import { IconName } from '../shared/icon';

/**
 * Weights, exams and checkups for the Saúde tab. Every change returns the whole data set from the
 * API, which replaces the local copy. Statuses are worked out here, for any "as of" date.
 */
@Injectable({ providedIn: 'root' })
export class Health {
  private readonly http = inject(HttpClient);
  readonly res = pollingResource<HealthData>('/api/health', 5 * MINUTE);

  readonly data = computed(() => valueOf(this.res));
  readonly error = signal<string | null>(null);

  saveWeight(w: SaveWeight) {
    return this.apply(this.http.post<HealthData>('/api/health/weights', w));
  }

  removeWeight(id: string) {
    return this.apply(this.http.delete<HealthData>(`/api/health/weights/${id}`));
  }

  saveProfile(personId: string, p: SaveProfile) {
    return this.apply(this.http.put<HealthData>(`/api/health/profiles/${personId}`, p));
  }

  createExam(e: SaveExam) {
    return this.apply(this.http.post<HealthData>('/api/health/exams', e));
  }

  updateExam(id: string, e: SaveExam) {
    return this.apply(this.http.put<HealthData>(`/api/health/exams/${id}`, e));
  }

  removeExam(id: string) {
    return this.apply(this.http.delete<HealthData>(`/api/health/exams/${id}`));
  }

  createCheckup(c: SaveCheckup) {
    return this.apply(this.http.post<HealthData>('/api/health/checkups', c));
  }

  updateCheckup(id: string, c: SaveCheckup) {
    return this.apply(this.http.put<HealthData>(`/api/health/checkups/${id}`, c));
  }

  markCheckupDone(id: string, date: string) {
    return this.apply(this.http.post<HealthData>(`/api/health/checkups/${id}/done`, { date }));
  }

  removeCheckup(id: string) {
    return this.apply(this.http.delete<HealthData>(`/api/health/checkups/${id}`));
  }

  private apply(request: Observable<HealthData>): Promise<boolean> {
    return new Promise((resolve) =>
      request.subscribe({
        next: (data) => {
          this.error.set(null);
          this.res.set(data);
          resolve(true);
        },
        error: (e) => {
          this.error.set(saveErrorMessage(e));
          this.res.reload();
          resolve(false);
        },
      }),
    );
  }
}

export const EXAM_KINDS: { value: ExamKind; label: string; icon: IconName }[] = [
  { value: 'sangue', label: 'Exame de sangue', icon: 'droplet' },
  { value: 'urina', label: 'Exame de urina', icon: 'flask' },
  { value: 'imagem', label: 'Imagem', icon: 'scan' },
  { value: 'cardio', label: 'Coração', icon: 'heart' },
  { value: 'consulta', label: 'Consulta', icon: 'stethoscope' },
  { value: 'medidas', label: 'Medidas (pressão etc.)', icon: 'activity' },
  { value: 'outro', label: 'Outro', icon: 'flask' },
];

export function examKindOf(kind: ExamKind) {
  return EXAM_KINDS.find((k) => k.value === kind) ?? EXAM_KINDS[EXAM_KINDS.length - 1];
}

/** ok inside the range; atenção when outside by up to 10% of the bound it crossed; alterado beyond that. */
export type MarkerStatus = 'ok' | 'atencao' | 'alterado';

/** How far outside its reference range a value counts as "atenção" rather than "alterado". */
const BORDERLINE = 0.1;

export function markerStatus(value: number, low: number | null, high: number | null): MarkerStatus {
  const off = low !== null && value < low ? (low - value) / low : high !== null && value > high ? (value - high) / high : 0;
  return off === 0 ? 'ok' : off <= BORDERLINE ? 'atencao' : 'alterado';
}

export const STATUS_LABEL: Record<MarkerStatus, string> = { ok: 'ok', atencao: 'atenção', alterado: 'alterado' };
const STATUS_RANK: Record<MarkerStatus, number> = { ok: 0, atencao: 1, alterado: 2 };

export function worst(statuses: MarkerStatus[]): MarkerStatus | null {
  return statuses.length ? statuses.reduce((a, b) => (STATUS_RANK[b] > STATUS_RANK[a] ? b : a)) : null;
}

/** "< 130", "40 – 100", "> 40" */
export function rangeLabel(low: number | null, high: number | null): string {
  if (low !== null && high !== null) return `${num(low)} – ${num(high)}`;
  if (high !== null) return `até ${num(high)}`;
  if (low !== null) return `acima de ${num(low)}`;
  return '—';
}

const numFmt = new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 2 });
export const num = (n: number) => numFmt.format(n);
const kgFmt = new Intl.NumberFormat('pt-BR', { minimumFractionDigits: 1, maximumFractionDigits: 1 });
export const kg = (n: number) => kgFmt.format(n);
export const signedKg = (n: number) => `${n > 0 ? '+' : n < 0 ? '−' : ''}${kgFmt.format(Math.abs(n))}`;

/** A marker's readings for one person up to a date, newest last, with the status of the latest. */
export interface MarkerReading {
  marker: Marker;
  personId: string;
  latest: { date: string; value: number; low: number | null; high: number | null; status: MarkerStatus };
  history: { date: string; value: number }[];
}

/** Every marker measured for a person up to (and including) asOf, keyed by marker code. */
export function readingsFor(data: HealthData, personId: string, asOf: string): Map<string, MarkerReading> {
  const markers = new Map(data.markers.map((m) => [m.code, m]));
  const out = new Map<string, MarkerReading>();
  // Exams arrive newest first; walk oldest first so the last write is the latest reading.
  const exams = data.exams.filter((e) => e.personId === personId && e.date <= asOf).slice().reverse();
  for (const e of exams) {
    for (const r of e.results) {
      const marker = markers.get(r.marker);
      if (!marker) continue;
      const low = r.refLow ?? marker.refLow;
      const high = r.refHigh ?? marker.refHigh;
      const prev = out.get(r.marker);
      out.set(r.marker, {
        marker,
        personId,
        latest: { date: e.date, value: r.value, low, high, status: markerStatus(r.value, low, high) },
        history: [...(prev?.history ?? []), { date: e.date, value: r.value }],
      });
    }
  }
  return out;
}

/** Weigh-ins for one person in [from, to], oldest first. */
export function weightsFor(weights: WeightEntry[], personId: string, from: string, to: string) {
  return weights.filter((w) => w.personId === personId && w.date >= from && w.date <= to);
}

/** Average of the weigh-ins in the 7 days ending on `day` (daily weight is noisy; the trend isn't). */
export function weekAverage(weights: WeightEntry[], personId: string, day: string): number | null {
  const list = weightsFor(weights, personId, addDays(day, -6), day);
  return list.length ? list.reduce((s, w) => s + w.kg, 0) / list.length : null;
}

/** When a checkup is due: last time + its interval. Never done = due now. */
export function checkupDue(c: Checkup, today: string): { date: string | null; days: number } {
  if (!c.lastDone) return { date: null, days: 0 };
  const d = new Date(c.lastDone);
  d.setUTCMonth(d.getUTCMonth() + c.everyMonths);
  const date = d.toISOString().slice(0, 10);
  return { date, days: Math.round((Date.parse(date) - Date.parse(today)) / 86_400_000) };
}

export function addDays(day: string, days: number): string {
  const d = new Date(day);
  d.setUTCDate(d.getUTCDate() + days);
  return d.toISOString().slice(0, 10);
}
