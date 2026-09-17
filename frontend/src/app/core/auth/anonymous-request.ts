import { HttpContext, HttpContextToken } from '@angular/common/http';

/**
 * Marks a request that must go out with no Authorization header.
 *
 * Logging in is the case that matters: the venue is resolved from the slug in
 * the path, but the API prefers the token's venue claim when one is present.
 * A leftover session from another venue would therefore silently redirect the
 * login to the wrong venue's staff table.
 */
export const ANONYMOUS = new HttpContextToken<boolean>(() => false);

export function anonymously(): HttpContext {
  return new HttpContext().set(ANONYMOUS, true);
}
