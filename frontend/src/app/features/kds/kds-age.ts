/** How urgent a card looks, from green to red (US-15, criterion 2). */
export type AgeBand = 'ok' | 'warn' | 'urg';

/**
 * The thresholds the wireframe draws: normal under 5 minutes, ámbar at 5,
 * rojo at 10. Both boundaries are inclusive on the way up — "a los 5" is
 * already ámbar, not the instant after.
 */
export function ageBandFor(minutes: number): AgeBand {
  if (minutes >= 10) return 'urg';
  if (minutes >= 5) return 'warn';

  return 'ok';
}

/**
 * What the card prints: just the minutes. The band is the card's color; the
 * word "urgente" that used to follow was dropped on 2026-09-28 as noise.
 */
export function ageLabelFor(minutes: number): string {
  const whole = Math.floor(minutes);

  return `${whole} min`;
}
