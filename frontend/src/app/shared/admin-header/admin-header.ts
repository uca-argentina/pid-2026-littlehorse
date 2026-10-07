import { Component, input } from '@angular/core';
import { StaffHeader } from '../staff-header/staff-header';
import type { StaffSection } from '../staff-header/staff-header';

/** The administration screens, reachable from each other. */
const ADMINISTRATION: readonly StaffSection[] = [
  { label: 'Productos', path: 'products' },
  { label: 'Staff', path: 'users' },
  { label: 'Noches', path: 'nights' },
];

/** The staff header as every administration screen draws it. */
@Component({
  selector: 'drinkit-admin-header',
  imports: [StaffHeader],
  template: `<drinkit-staff-header
    [venueSlug]="venueSlug()"
    context="Administración"
    [sections]="sections"
  />`,
})
export class AdminHeader {
  readonly venueSlug = input.required<string>();

  protected readonly sections = ADMINISTRATION;
}
