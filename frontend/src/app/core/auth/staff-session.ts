import type { components } from '../api/schema';

/**
 * The session as the API hands it over. Taken from the generated contract so it
 * cannot drift from the backend: nothing here is typed by hand.
 */
export type StaffSession = components['schemas']['LoginResponse'];

export type StaffCredentials = components['schemas']['LoginRequest'];
