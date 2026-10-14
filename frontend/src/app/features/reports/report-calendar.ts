import { phaseOf } from '../nights/night-calendar';

interface NightHours {
  readonly id: string;
  readonly startsAt: string;
  readonly endsAt: string;
}

/**
 * The nights with something to report: underway or over, the latest first. A
 * night that has not started has no orders yet.
 */
export function reportableNights<T extends NightHours>(nights: readonly T[], now: Date): T[] {
  return nights
    .filter((night) => phaseOf(night, now) !== 'upcoming')
    .sort((a, b) => Date.parse(b.startsAt) - Date.parse(a.startsAt));
}

/**
 * The night the Reportes tab opens on: the one underway if there is one, the
 * last one that ended if not. Nights never overlap, so the latest that started
 * is whichever of the two applies.
 */
export function nightToOpen<T extends NightHours>(nights: readonly T[], now: Date): T | null {
  return reportableNights(nights, now)[0] ?? null;
}

/** "7 min". Under a minute says so, and nothing measured is a dash, never "0 min". */
export function formatMinutes(seconds: number | null): string {
  if (seconds === null) return '—';
  if (seconds < 60) return 'menos de 1 min';

  return `${Math.round(seconds / 60)} min`;
}
