import { HttpErrorResponse } from '@angular/common/http';

/**
 * The drink a problem names, when it names one: the API puts it beside the
 * type when something ran out or left the menu. Never the detail, which is the
 * API's sentence, in English, for developers.
 */
export function problemProductNameOf(error: unknown): string | null {
  const body: unknown = error instanceof HttpErrorResponse ? error.error : null;

  return typeof body === 'object' && body !== null && 'productName' in body
    ? String((body as { productName: unknown }).productName)
    : null;
}
