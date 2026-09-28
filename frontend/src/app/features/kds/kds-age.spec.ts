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

  it('reads an urgent wait with the word that makes it impossible to miss', () => {
    expect(ageLabelFor(11)).toBe('11 min · urgente');
  });

  it('rounds down instead of showing a decimal nobody asked for', () => {
    expect(ageLabelFor(2.9)).toBe('2 min');
  });
});
