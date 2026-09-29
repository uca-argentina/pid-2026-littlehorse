import type { components } from '../api/schema';

/**
 * The roles a venue can hand out, and what each one is called on screen. The
 * API speaks English and the venue does not.
 *
 * The names come from the generated contract: a role written here that the
 * API does not hand out fails to compile.
 *
 * There is no "bartender": the KDS is the bar station's own account, shared by
 * everyone preparing there, because the tablet belongs to the station and not
 * to a person (functional design, §11).
 */
export type StaffRole = components['schemas']['StaffRoleName'];

export const STAFF_ROLES = [
  'Administrator',
  'Kds',
  'Waiter',
] as const satisfies readonly StaffRole[];

export interface StaffRoleDescription {
  readonly role: StaffRole;
  readonly name: string;
  /** How a filter over the whole team names this role. */
  readonly plural: string;
  /** What this role can do, in the words an administrator would use. */
  readonly does: string;
}

export const STAFF_ROLE_DESCRIPTIONS: readonly StaffRoleDescription[] = [
  {
    role: 'Administrator',
    name: 'Administrador',
    plural: 'Administradores',
    does: 'Carga la carta y da de alta al resto del equipo.',
  },
  {
    role: 'Kds',
    name: 'KDS · estación de barra',
    // The station is one account, so it never reads as a plural.
    plural: 'KDS',
    does: 'La cuenta de la tablet de la barra, compartida por quienes preparan ahí.',
  },
  {
    role: 'Waiter',
    name: 'Mozo',
    plural: 'Mozos',
    does: 'Entrega pedidos en las mesas del sector VIP.',
  },
];

const NAMES = new Map<string, string>(
  STAFF_ROLE_DESCRIPTIONS.map((description) => [description.role, description.name]),
);

/**
 * Falls back to whatever the API sent rather than to "desconocido": a role
 * added on the server and not yet here should read as itself, not as a bug.
 */
export function staffRoleName(role: string): string {
  return NAMES.get(role) ?? role;
}
