import type { HttpInterceptorFn } from '@angular/common/http';
import { InjectionToken, inject } from '@angular/core';

/**
 * Replaced at build time by `ng build --define`, from the API address the
 * deploy reads out of the Bicep outputs. Absent in development and in tests,
 * where `typeof` still answers safely.
 */
declare const DRINKIT_API_BASE_URL: string | undefined;

/**
 * Where the API lives, with no trailing slash. Empty means same origin: the
 * Angular proxy in development, which strips /api on its own.
 */
export const API_BASE_URL = new InjectionToken<string>('API_BASE_URL', {
  factory: () => (typeof DRINKIT_API_BASE_URL === 'string' ? DRINKIT_API_BASE_URL : ''),
});

const PREFIX = '/api';

/**
 * Deployed, the PWA and the API are on different hosts and nothing forwards
 * /api between them, so every call written as /api/... is sent to the API's
 * own address instead. The services keep writing relative paths either way.
 */
export const apiBaseUrlInterceptor: HttpInterceptorFn = (request, next) => {
  const baseUrl = inject(API_BASE_URL);

  if (baseUrl === '' || !request.url.startsWith(`${PREFIX}/`)) return next(request);

  return next(request.clone({ url: baseUrl + request.url.slice(PREFIX.length) }));
};
