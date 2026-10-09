/** Where a night stands against a moment. */
export type NightPhase = 'upcoming' | 'underway' | 'over';

interface NightHours {
  readonly startsAt: string;
  readonly endsAt: string;
}

const HOURS_IN_MS = 60 * 60 * 1000;

/** Same rule as Night.IsUnderwayAt on the server: the end is exclusive. */
export function phaseOf(night: NightHours, now: Date): NightPhase {
  const moment = now.getTime();

  if (moment < Date.parse(night.startsAt)) return 'upcoming';
  if (moment < Date.parse(night.endsAt)) return 'underway';

  return 'over';
}

const day = new Intl.DateTimeFormat('es-AR', {
  weekday: 'short',
  day: '2-digit',
  month: '2-digit',
});
const time = new Intl.DateTimeFormat('es-AR', {
  hour: '2-digit',
  minute: '2-digit',
  hourCycle: 'h23',
});

/**
 * "sáb 10/10 · 23:00 a 06:00". The end's own date only shows when it is more
 * than a day away: a night crossing midnight is the normal case, not news.
 */
export function nightHours(night: NightHours): string {
  const start = new Date(night.startsAt);
  const end = new Date(night.endsAt);
  const endsWithinADay = end.getTime() - start.getTime() <= 24 * HOURS_IN_MS;

  const until = endsWithinADay ? time.format(end) : `${dayOf(end)} ${time.format(end)}`;

  return `${dayOf(start)} · ${time.format(start)} a ${until}`;
}

/**
 * "sáb 10/10", put together from its parts: how es-AR joins them (a comma, a
 * period after the weekday, a dash or a slash) changes between ICU versions,
 * and so between browsers.
 */
function dayOf(date: Date): string {
  const parts = new Map(day.formatToParts(date).map((part) => [part.type, part.value]));

  return `${parts.get('weekday')?.replace('.', '')} ${parts.get('day')}/${parts.get('month')}`;
}
