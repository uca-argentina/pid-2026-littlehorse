import { roleGuard } from './role-guard';

/** Keeps the bar's board to the bar's own account (US-15, criterion 3). */
export const kdsGuard = roleGuard('Kds');
