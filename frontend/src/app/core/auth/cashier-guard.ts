import { roleGuard } from './role-guard';

/** Keeps the till to the cashier's own account (US-26). */
export const cashierGuard = roleGuard('Cashier');
