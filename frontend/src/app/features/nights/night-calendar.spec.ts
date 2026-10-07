import { nightHours, phaseOf } from './night-calendar';

// Built in local time on purpose, like the screen reads them: the assertions
// then hold on a machine in any timezone.
const opening = new Date(2026, 9, 10, 23, 0);
const closing = new Date(2026, 9, 11, 6, 0);
const saturday = { startsAt: opening.toISOString(), endsAt: closing.toISOString() };

describe('phaseOf', () => {
  it('is upcoming before the night starts', () => {
    expect(phaseOf(saturday, new Date(2026, 9, 10, 22, 59))).toBe('upcoming');
  });

  it('is underway from the very minute it starts', () => {
    expect(phaseOf(saturday, opening)).toBe('underway');
  });

  // The end is exclusive, as on the server: at 06:00 sharp it takes no orders.
  it('is over from the very minute it ends', () => {
    expect(phaseOf(saturday, closing)).toBe('over');
  });
});

describe('nightHours', () => {
  // A night crosses midnight: the end alone would read as the same day.
  it('names the day the night starts and both hours', () => {
    expect(nightHours(saturday)).toBe('sáb 10/10 · 23:00 a 06:00');
  });

  // Longer than a day is rare, but then the end needs its own date.
  it('names the end day too when it ends more than a day later', () => {
    const long = {
      startsAt: opening.toISOString(),
      endsAt: new Date(2026, 9, 12, 6, 0).toISOString(),
    };

    expect(nightHours(long)).toBe('sáb 10/10 · 23:00 a lun 12/10 06:00');
  });
});
