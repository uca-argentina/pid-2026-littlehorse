import type { HttpInterceptorFn } from '@angular/common/http';
import { HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { throwError } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { ProblemTypes } from '../api/problem-types';
import { ANONYMOUS } from './anonymous-request';
import { SessionStorage } from './session-storage';

/**
 * Carries the shift's token on every request and drops the session the moment
 * the API says it is no longer good. Without the second half the app would keep
 * firing requests with a dead token for the rest of the night, with no way back
 * to the login screen.
 */
export const authenticationInterceptor: HttpInterceptorFn = (request, next) => {
  const sessions = inject(SessionStorage);
  const session = sessions.session();
  const mustStayAnonymous = request.context.get(ANONYMOUS);

  const authenticated =
    session === null || mustStayAnonymous
      ? request
      : request.clone({ setHeaders: { Authorization: `Bearer ${session.token}` } });

  return next(authenticated).pipe(
    catchError((error: unknown) => {
      if (isRejectedToken(error)) sessions.forget('expired');

      return throwError(() => error);
    }),
  );
};

/**
 * Both of these mean the same thing to us: the token we are holding is no good.
 * They are distinct problems on purpose (ADR-0009) — one is a token past its
 * eight hours, the other a request that arrived without a usable one — and wrong
 * credentials share the same status code without meaning either.
 */
function isRejectedToken(error: unknown): boolean {
  if (!(error instanceof HttpErrorResponse) || error.status !== 401) return false;

  const type = error.error?.type;

  return type === ProblemTypes.sessionExpired || type === ProblemTypes.authenticationRequired;
}
