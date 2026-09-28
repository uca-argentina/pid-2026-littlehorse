import { effect, signal } from '@angular/core';
import type { Signal } from '@angular/core';

const SLOW_AFTER_MS = 5000;

/**
 * True once something has been loading for a few seconds, false again as soon
 * as it stops. A cold start of the API and the database together takes most
 * of a minute, and a skeleton that sits still that long looks frozen.
 *
 * Call it where inject() works: a field initializer or a constructor.
 */
export function slowLoading(isLoading: () => boolean): Signal<boolean> {
  const isSlow = signal(false);

  effect((onCleanup) => {
    isSlow.set(false);
    if (!isLoading()) return;

    const timer = setTimeout(() => isSlow.set(true), SLOW_AFTER_MS);
    onCleanup(() => clearTimeout(timer));
  });

  return isSlow.asReadonly();
}
