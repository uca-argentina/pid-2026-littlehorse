import { HttpErrorResponse, httpResource } from '@angular/common/http';
import { Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ProblemTypes } from '../../../core/api/problem-types';
import { SessionStorage } from '../../../core/auth/session-storage';
import { staffRoleName } from '../../../core/staff/staff-roles';
import { SignOut } from '../../../shared/sign-out/sign-out';
import { VenueBrand } from '../../../shared/venue-brand/venue-brand';
import { STAFF_USERS_URL } from '../staff-users.service';
import type { StaffUser } from '../staff-users.service';

/** One row of the listing, with the role already in the venue's language. */
interface StaffRow {
  readonly id: string;
  readonly username: string;
  readonly roleName: string;
  readonly isActive: boolean;
}

/** Why the listing is not on screen. */
type ListingFailure = 'none' | 'forbidden' | 'unreachable';

@Component({
  selector: 'drinkit-staff-users-page',
  imports: [RouterLink, SignOut, VenueBrand],
  styleUrl: './staff-users.page.scss',
  templateUrl: './staff-users.page.html',
})
export class StaffUsersPage {
  private readonly sessions = inject(SessionStorage);

  /** From the path. Bound by the router, so the screen never asks for a venue. */
  readonly venueSlug = input.required<string>();

  /**
   * The Resource API rather than a subscription: loading, error and value
   * arrive as signals, which is exactly the three states this screen draws.
   */
  protected readonly staff = httpResource<StaffUser[]>(() => STAFF_USERS_URL);

  // hasValue() and not value(): reading the value of a failed resource throws,
  // and the template reads this on every change detection, error state included.
  protected readonly rows = computed<StaffRow[]>(() =>
    (this.staff.hasValue() ? this.staff.value() : []).map((user) => ({
      id: user.id,
      username: user.username,
      roleName: staffRoleName(user.role),
      isActive: user.isActive,
    })),
  );

  /** Who is doing the managing, so a shared laptop never hides whose account it is. */
  protected readonly username = computed(() => this.sessions.session()?.username ?? '');

  /**
   * The two failures are worth telling apart: one goes away when the signal
   * comes back, and the other never will, however many times they retry.
   */
  protected readonly failure = computed<ListingFailure>(() => {
    const error = this.staff.error();

    if (error === undefined) return 'none';

    return problemTypeOf(error) === ProblemTypes.forbidden ? 'forbidden' : 'unreachable';
  });

  /** Nothing to draw, and not because it is still loading or because it broke. */
  protected readonly isEmpty = computed(() => this.staff.hasValue() && this.rows().length === 0);
}

/**
 * A resource reports the failure wrapped, keeping the original underneath in
 * "cause", so the response has to be dug out rather than cast.
 */
function problemTypeOf(error: unknown): string | undefined {
  const response = error instanceof HttpErrorResponse ? error : (error as Error | null)?.cause;

  return response instanceof HttpErrorResponse
    ? (response.error as { type?: string } | null)?.type
    : undefined;
}
