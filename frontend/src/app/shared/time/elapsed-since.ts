/**
 * How long ago something happened, as a phone or a till reads it: "40 seg"
 * under a minute, "9 min" from there on. Never negative: a clock a little
 * behind the server's would otherwise print a moment in the future.
 */
export function elapsedSince(now: number, moment: string | null): string {
  if (moment === null) return '';

  const seconds = Math.max(0, Math.floor((now - Date.parse(moment)) / 1000));

  return seconds < 60 ? `${seconds} seg` : `${Math.floor(seconds / 60)} min`;
}
