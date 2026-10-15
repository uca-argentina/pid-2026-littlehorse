import { formatMinutes, nightToOpen, reportableNights } from './report-calendar';

const friday = {
  id: 'friday',
  startsAt: new Date(2026, 9, 9, 23, 0).toISOString(),
  endsAt: new Date(2026, 9, 10, 6, 0).toISOString(),
};
const saturday = {
  id: 'saturday',
  startsAt: new Date(2026, 9, 10, 23, 0).toISOString(),
  endsAt: new Date(2026, 9, 11, 6, 0).toISOString(),
};
const nextFriday = {
  id: 'next-friday',
  startsAt: new Date(2026, 9, 16, 23, 0).toISOString(),
  endsAt: new Date(2026, 9, 17, 6, 0).toISOString(),
};

const duringSaturday = new Date(2026, 9, 11, 1, 0);
const onSunday = new Date(2026, 9, 11, 15, 0);

describe('reportableNights', () => {
  // A night that has not started has no orders to report on.
  it('leaves out the nights that have not started', () => {
    expect(reportableNights([nextFriday, saturday, friday], onSunday).map((n) => n.id)).toEqual([
      'saturday',
      'friday',
    ]);
  });

  it('puts the latest first, whatever order they came in', () => {
    expect(reportableNights([friday, saturday], onSunday).map((n) => n.id)).toEqual([
      'saturday',
      'friday',
    ]);
  });
});

describe('nightToOpen', () => {
  it('is the night underway when there is one', () => {
    expect(nightToOpen([friday, saturday, nextFriday], duringSaturday)?.id).toBe('saturday');
  });

  it('is the last one that ended when none is underway', () => {
    expect(nightToOpen([friday, saturday, nextFriday], onSunday)?.id).toBe('saturday');
  });

  it('is none when no night has started yet', () => {
    expect(nightToOpen([nextFriday], onSunday)).toBeNull();
  });
});

describe('formatMinutes', () => {
  it('rounds to whole minutes', () => {
    expect(formatMinutes(430)).toBe('7 min');
  });

  // "0 min" would read as instant, or as broken.
  it('says less than a minute below one', () => {
    expect(formatMinutes(40)).toBe('menos de 1 min');
  });

  // No order got there yet: nothing to show, which is not zero.
  it('shows a dash when there is nothing measured', () => {
    expect(formatMinutes(null)).toBe('—');
  });
});
