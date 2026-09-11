import { Component, computed, inject, input } from '@angular/core';
import { Router } from '@angular/router';
import { VenueBrand } from '../../../shared/venue-brand/venue-brand';
import { SessionStorage } from '../../../core/auth/session-storage';

/** What each role is called on screen. The API speaks English; the venue does not. */
const ROLE_NAMES: Record<string, string> = {
  Administrator: 'Administrador',
  Bartender: 'KDS · estación de barra',
};

/**
 * Where everyone lands after signing in, until the screens for each role exist.
 * It is not a placeholder: a person who signs in and sees nothing cannot tell a
 * working system from a broken one.
 */
@Component({
  selector: 'drinkit-staff-home-page',
  imports: [VenueBrand],
  styleUrl: './staff-home.page.scss',
  templateUrl: './staff-home.page.html',
})
export class StaffHomePage {
  private readonly sessions = inject(SessionStorage);

  private readonly router = inject(Router);

  readonly venueSlug = input.required<string>();

  protected readonly username = computed(() => this.sessions.session()?.username ?? '');

  protected readonly roleName = computed(() => {
    const role = this.sessions.session()?.role ?? '';

    return ROLE_NAMES[role] ?? role;
  });

  protected leave(): void {
    this.sessions.forget();
    void this.router.navigate([this.venueSlug(), 'staff', 'login']);
  }
}
