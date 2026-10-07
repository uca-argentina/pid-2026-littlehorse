import { httpResource } from '@angular/common/http';
import { Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AdminHeader } from '../../../shared/admin-header/admin-header';
import { STAFF_USERS_URL } from '../../staff-users/staff-users.service';
import type { StaffUser } from '../../staff-users/staff-users.service';
import { NightForm } from '../components/night-form/night-form';
import type { NewNight } from '../nights.service';
import { SaveNightStore, refusalFor } from '../save-night.store';

@Component({
  selector: 'drinkit-new-night-page',
  imports: [AdminHeader, NightForm, RouterLink],
  // On the component, as in NewStaffUserPage: a stale failure dies with it.
  providers: [SaveNightStore],
  styleUrl: './night-pages.scss',
  templateUrl: './new-night.page.html',
})
export class NewNightPage {
  protected readonly store = inject(SaveNightStore);

  /** From the path. Bound by the router, so the screen never asks for a venue. */
  readonly venueSlug = input.required<string>();

  /** The whole team, for the crew dropdowns. */
  protected readonly team = httpResource<StaffUser[]>(() => STAFF_USERS_URL);

  protected readonly refusal = computed(() => refusalFor(this.store.status()));

  protected save(night: NewNight): void {
    this.store.create(this.venueSlug(), night);
  }
}
