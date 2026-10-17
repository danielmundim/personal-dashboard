import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';

export const LOCALE = 'pt-BR';
export const TIME_ZONE = 'Europe/Lisbon';

const timeFmt = new Intl.DateTimeFormat(LOCALE, { hour: '2-digit', minute: '2-digit', timeZone: TIME_ZONE });
const hourFmt = new Intl.DateTimeFormat(LOCALE, { hour: '2-digit', timeZone: TIME_ZONE });
const weekdayFmt = new Intl.DateTimeFormat(LOCALE, { weekday: 'short', timeZone: TIME_ZONE });
const longDateFmt = new Intl.DateTimeFormat(LOCALE, { weekday: 'long', day: 'numeric', month: 'long', timeZone: TIME_ZONE });
const dayKeyFmt = new Intl.DateTimeFormat('en-CA', { timeZone: TIME_ZONE }); // yyyy-mm-dd, locale-independent key
// Date-only values (DateOnly from the API, "2026-11-01") parse as UTC midnight, so format them in UTC.
const dateOnlyWeekdayFmt = new Intl.DateTimeFormat(LOCALE, { weekday: 'long', timeZone: 'UTC' });
const dateOnlyShortWeekdayFmt = new Intl.DateTimeFormat(LOCALE, { weekday: 'short', timeZone: 'UTC' });
const dateOnlyMonthShortFmt = new Intl.DateTimeFormat(LOCALE, { month: 'short', timeZone: 'UTC' });
const dateOnlyDayMonthFmt = new Intl.DateTimeFormat(LOCALE, { day: 'numeric', month: 'long', timeZone: 'UTC' });
const dayNumFmt = new Intl.DateTimeFormat(LOCALE, { day: '2-digit', timeZone: TIME_ZONE });
const monthShortFmt = new Intl.DateTimeFormat(LOCALE, { month: 'short', timeZone: TIME_ZONE });

/** pt-BR short names come as "sáb." / "out."; on tiles we want "Sáb" / "out". */
const short = (s: string) => s.replace(/\.$/, '');
const capitalize = (s: string) => s.charAt(0).toLocaleUpperCase(LOCALE) + s.slice(1);

export const fmt = {
  time: (iso: string | Date) => timeFmt.format(new Date(iso)),
  hour: (iso: string | Date) => hourFmt.format(new Date(iso)),
  weekday: (iso: string | Date) => capitalize(short(weekdayFmt.format(new Date(iso)))),
  longDate: (d: Date) => capitalize(longDateFmt.format(d)),
  /** "Seg 19 out" */
  shortDate: (iso: string | Date) => `${fmt.weekday(iso)} ${Number(dayNumFmt.format(new Date(iso)))} ${fmt.monthShort(iso)}`,
  dayKey: (iso: string | Date) => dayKeyFmt.format(new Date(iso)),
  dayNum: (iso: string | Date) => dayNumFmt.format(new Date(iso)),
  monthShort: (iso: string | Date) => short(monthShortFmt.format(new Date(iso))),
  dateOnlyWeekday: (d: string) => capitalize(dateOnlyWeekdayFmt.format(new Date(d))),
  dateOnlyShortWeekday: (d: string) => capitalize(short(dateOnlyShortWeekdayFmt.format(new Date(d)))),
  dateOnlyMonthShort: (d: string) => short(dateOnlyMonthShortFmt.format(new Date(d))),
  /** "Domingo, 1 de novembro" */
  dateOnlyFull: (d: string) => `${capitalize(dateOnlyWeekdayFmt.format(new Date(d)))}, ${dateOnlyDayMonthFmt.format(new Date(d))}`,
};

/** "Hoje", "Amanhã", "Seg 20 out" for an ISO timestamp, relative to now in the household zone. */
export function relativeDay(iso: string, now: Date): string {
  const days = daysUntil(fmt.dayKey(iso), now);
  return days === 0 ? 'Hoje' : days === 1 ? 'Amanhã' : fmt.shortDate(iso);
}

/** "em 5 min", "em 2 h 10 min", "amanhã 09:30", "Sáb 18:00" style countdown for an ISO timestamp. */
export function startsIn(iso: string, now: Date): string {
  const mins = Math.round((new Date(iso).getTime() - now.getTime()) / 60_000);
  if (mins <= 0) return 'agora';
  if (mins < 60) return `em ${mins} min`;
  if (mins < 12 * 60) return `em ${Math.floor(mins / 60)} h ${mins % 60 ? (mins % 60) + ' min' : ''}`.trim();
  return `${relativeDay(iso, now) === 'Amanhã' ? 'amanhã' : fmt.weekday(iso)} ${fmt.time(iso)}`;
}

/** "hoje", "amanhã", "em 12 dias" for a whole-day count. */
export function inDays(days: number): string {
  return days <= 0 ? 'hoje' : days === 1 ? 'amanhã' : `em ${days} dias`;
}

/** Whole days from today to a yyyy-mm-dd date (DateOnly from the API), in the household zone. */
export function daysUntil(dateOnly: string, now: Date): number {
  const today = Date.parse(fmt.dayKey(now));
  return Math.round((Date.parse(dateOnly) - today) / 86_400_000);
}

/** Ticks once per second; everything time-relative on the screen derives from this. */
@Injectable({ providedIn: 'root' })
export class Clock {
  readonly now = signal(new Date());
  readonly time = computed(() => fmt.time(this.now()));
  readonly date = computed(() => fmt.longDate(this.now()));
  readonly greeting = computed(() => {
    const h = Number(fmt.hour(this.now()));
    return h < 5 ? 'Boa noite' : h < 12 ? 'Bom dia' : h < 19 ? 'Boa tarde' : 'Boa noite';
  });

  constructor() {
    const id = setInterval(() => this.now.set(new Date()), 1000);
    inject(DestroyRef).onDestroy(() => clearInterval(id));
  }
}
