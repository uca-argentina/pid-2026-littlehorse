/** Faster than anybody types: a USB reader sends a whole QR in a few milliseconds. */
const READER_GAP_MS = 50;

/** Shorter than any code, so a stray key and Enter are not a read. */
const SHORTEST_READ = 4;

/**
 * Tells a USB reader apart from a person, for when the cursor is not in the
 * till's field. A reader is a keyboard (HID): it types what it read and
 * presses Enter, all in one burst. Keys closer together than any person types
 * are gathered; a pause throws them away; Enter hands over what was gathered.
 */
// ponytail: timing heuristic; a reader configured with a prefix character would make it exact.
export class ReaderBurst {
  private typed = '';

  private last = Number.NEGATIVE_INFINITY;

  /** Feeds one key. Returns what a reader typed when this key ends a burst, or null. */
  key(key: string, at: number): string | null {
    const inBurst = at - this.last <= READER_GAP_MS;

    this.last = at;

    if (key === 'Enter') {
      const read = inBurst && this.typed.length >= SHORTEST_READ ? this.typed : null;

      this.typed = '';

      return read;
    }

    // Shift, Tab, arrows: nothing typed, and nothing to break the burst either.
    if (key.length !== 1) return null;

    this.typed = inBurst ? this.typed + key : key;

    return null;
  }
}
