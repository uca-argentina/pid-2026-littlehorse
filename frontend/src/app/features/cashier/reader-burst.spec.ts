import { ReaderBurst } from './reader-burst';

/** Feeds the keys one after the other, `gap` milliseconds apart. */
function type(burst: ReaderBurst, text: string, gap: number, from = 0): string | null {
  let at = from;
  let read: string | null = null;

  for (const key of [...text, 'Enter']) {
    read = burst.key(key, at);
    at += gap;
  }

  return read;
}

describe('ReaderBurst', () => {
  // A USB reader is a keyboard that types a whole QR in a few milliseconds.
  it('hands over what a reader typed, once it presses Enter', () => {
    expect(type(new ReaderBurst(), '9f3c2ba7d81e4c06a1b2c3d4e5f60718', 5)).toBe(
      '9f3c2ba7d81e4c06a1b2c3d4e5f60718',
    );
  });

  // A person types a key every hundred milliseconds or more: that is not a read.
  it('ignores somebody typing by hand', () => {
    expect(type(new ReaderBurst(), 'A-0042', 150)).toBeNull();
  });

  it('ignores an Enter with next to nothing before it', () => {
    expect(type(new ReaderBurst(), 'ab', 5)).toBeNull();
  });

  // Stray keys a while ago must not glue themselves to the next read.
  it('starts over after a pause', () => {
    const burst = new ReaderBurst();

    burst.key('x', 0);
    burst.key('y', 5);

    expect(type(burst, 'A-0042', 5, 1000)).toBe('A-0042');
  });

  it('ignores keys that type nothing, like Shift', () => {
    const burst = new ReaderBurst();

    burst.key('Shift', 0);

    expect(type(burst, 'A-0042', 5, 1)).toBe('A-0042');
  });
});
