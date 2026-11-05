import { signal } from '@angular/core';
import type { StampCards } from './stamp-cards';

/**
 * A venue whose stamp card is off, for the specs of screens that draw one only
 * as a side dish: nothing is asked and nothing is shown, so a spec about
 * something else does not have to answer a request it never cared about.
 */
export function noStampCard(): Pick<
  StampCards,
  'open' | 'view' | 'refresh' | 'tokenOf' | 'remember'
> {
  return {
    open: () => undefined,
    view: signal(null).asReadonly(),
    refresh: () => undefined,
    tokenOf: () => null,
    remember: () => undefined,
  };
}
