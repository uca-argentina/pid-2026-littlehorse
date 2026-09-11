import { Component, effect, inject, input } from '@angular/core';
import { VenueBrand } from '../../../shared/venue-brand/venue-brand';
import { StaffLoginStore } from '../staff-login.store';

@Component({
  selector: 'drinkit-staff-login-page',
  imports: [VenueBrand],
  styleUrl: './staff-login.page.scss',
  templateUrl: './staff-login.page.html',
})
export class StaffLoginPage {
  protected readonly store = inject(StaffLoginStore);

  /** From the path. Bound by the router, so the screen never asks for a venue. */
  readonly venueSlug = input.required<string>();

  /** From the query string, set by the guard when it turned someone away. */
  readonly expired = input<string | undefined>(undefined);

  constructor() {
    effect(() => {
      if (this.expired() !== undefined) this.store.startAfterExpiry();
    });
  }

  protected submit(event: Event): void {
    event.preventDefault();
    this.store.submit(this.venueSlug());
  }

  protected readCurrentValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }
}
