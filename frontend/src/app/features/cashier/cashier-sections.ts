import type { StaffSection } from '../../shared/staff-header/staff-header';

/**
 * The till's two screens, reachable from each other (US-45): cash on the
 * counter, and the VIP tables waiting for a waiter. Kept apart from both
 * pages so neither drags the other's code into its own chunk.
 */
export const CASHIER_SECTIONS: readonly StaffSection[] = [
  { label: 'Cobro en efectivo', path: 'cashier' },
  { label: 'Entregas VIP', path: 'cashier/vip' },
];
