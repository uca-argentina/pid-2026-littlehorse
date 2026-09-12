import { Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { SessionStorage } from '../../../core/auth/session-storage';
import { staffRoleName } from '../../../core/staff/staff-roles';
import { SignOut } from '../../../shared/sign-out/sign-out';
import { VenueBrand } from '../../../shared/venue-brand/venue-brand';

/**
 * Where everyone lands after signing in. An administrator goes on from here to
 * what they manage; the other roles are told their screens do not exist yet,
 * because somebody who signs in and sees nothing cannot tell a working system
 * from a broken one.
 */
@Component({
  selector: 'drinkit-staff-home-page',
  imports: [RouterLink, SignOut, VenueBrand],
  styleUrl: './staff-home.page.scss',
  templateUrl: './staff-home.page.html',
})
export class StaffHomePage {
  private readonly sessions = inject(SessionStorage);

  readonly venueSlug = input.required<string>();

  protected readonly username = computed(() => this.sessions.session()?.username ?? '');

  protected readonly isAdministrator = computed(
    () => this.sessions.session()?.role === 'Administrator',
  );

  protected readonly roleName = computed(() => staffRoleName(this.sessions.session()?.role ?? ''));
}
