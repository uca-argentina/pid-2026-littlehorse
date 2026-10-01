import { ageBandFor, ageLabelFor } from './kds-age';

describe('ageBandFor', () => {
  // US-15, criterion 2: the three bands, at the exact minutes the criterion names.
  it.each([
    [0, 'ok'],
    [4.9, 'ok'],
    [5, 'warn'],
    [9.9, 'warn'],
    [10, 'urg'],
    [11, 'urg'],
  ] as const)('reads %s minutes as %s', (minutes, band) => {
    expect(ageBandFor(minutes)).toBe(band);
  });
});

describe('ageLabelFor', () => {
  it('reads a normal wait as just the minutes', () => {
    expect(ageLabelFor(2)).toBe('2 min');
  });

  // Decided on 2026-09-28: the red card says it; the word was noise.
  it('reads an urgent wait as just the minutes too', () => {
    expect(ageLabelFor(11)).toBe('11 min');
  });

  it('rounds down instead of showing a decimal nobody asked for', () => {
    expect(ageLabelFor(2.9)).toBe('2 min');
  });
});
