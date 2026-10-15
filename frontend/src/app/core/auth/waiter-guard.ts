import { roleGuard } from './role-guard';

/** Keeps the waiter's tables to the waiter's own account (US-43). */
export const waiterGuard = roleGuard('Waiter');
