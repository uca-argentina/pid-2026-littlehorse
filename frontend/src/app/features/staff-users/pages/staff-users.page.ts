import { HttpErrorResponse, httpResource } from '@angular/common/http';
import { Component, computed, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ProblemTypes } from '../../../core/api/problem-types';
import { STAFF_ROLE_DESCRIPTIONS, staffRoleName } from '../../../core/staff/staff-roles';
import type { StaffRole } from '../../../core/staff/staff-roles';
import { AdminHeader } from '../../../shared/admin-header/admin-header';
import { STAFF_USERS_URL } from '../staff-users.service';
import type { StaffUser } from '../staff-users.service';

/** One row of the listing, with the role already in the venue's language. */
interface StaffRow {
  readonly id: string;
  readonly username: string;
  readonly role: string;
  readonly roleName: string;
  readonly isActive: boolean;
}

/** Which role the listing is narrowed to, or everyone. */
type RoleFilter = StaffRole | 'all';

/** One of the buttons above the list, with what it would leave on screen. */
interface RoleTab {
  readonly filter: RoleFilter;
  readonly name: string;
  readonly count: number;
}

/** Why the listing is not on screen. */
type ListingFailure = 'none' | 'forbidden' | 'unreachable';

@Component({
  selector: 'drinkit-staff-users-page',
  imports: [AdminHeader, RouterLink],
  styleUrl: './staff-users.page.scss',
  templateUrl: './staff-users.page.html',
})
export class StaffUsersPage {
  /** From the path. Bound by the router, so the screen never asks for a venue. */
  readonly venueSlug = input.required<string>();

  /**
   * The Resource API rather than a subscription: loading, error and value
   * arrive as signals, which is exactly the three states this screen draws.
   */
  protected readonly staff = httpResource<StaffUser[]>(() => STAFF_USERS_URL);

  /**
   * What the administrator typed, and which role they narrowed to. Both are
   * applied here and not by the API: a venue's whole team fits in one response,
   * and asking the server on every keystroke would put the venue's connection
   * between somebody and the name they are looking at.
   */
  protected readonly search = signal('');

  protected readonly roleFilter = signal<RoleFilter>('all');

  // hasValue() and not value(): reading the value of a failed resource throws,
  // and the template reads this on every change detection, error state included.
  private readonly everyone = computed<StaffRow[]>(() =>
    (this.staff.hasValue() ? this.staff.value() : []).map((user) => ({
      id: user.id,
      username: user.username,
      role: user.role,
      roleName: staffRoleName(user.role),
      isActive: user.isActive,
    })),
  );

  /** Whoever matches what was typed, before the role narrows it further. */
  private readonly matchingTheSearch = computed(() => {
    const term = this.search().trim().toLowerCase();

    if (term === '') return this.everyone();

    return this.everyone().filter((row) => row.username.toLowerCase().includes(term));
  });

  protected readonly rows = computed(() => {
    const role = this.roleFilter();

    if (role === 'all') return this.matchingTheSearch();

    return this.matchingTheSearch().filter((row) => row.role === role);
  });

  /**
   * Counted over what the search left, not over the whole venue. A tab that
   * promises three and then shows none reads as a filter that is broken.
   */
  protected readonly roleTabs = computed<RoleTab[]>(() => {
    const matching = this.matchingTheSearch();

    return [
      { filter: 'all' as const, name: 'Todos', count: matching.length },
      ...STAFF_ROLE_DESCRIPTIONS.map((description) => ({
        filter: description.role,
        name: description.plural,
        count: matching.filter((row) => row.role === description.role).length,
      })),
    ];
  });

  /**
   * The two failures are worth telling apart: one goes away when the signal
   * comes back, and the other never will, however many times they retry.
   */
  protected readonly failure = computed<ListingFailure>(() => {
    const error = this.staff.error();

    if (error === undefined) return 'none';

    return problemTypeOf(error) === ProblemTypes.forbidden ? 'forbidden' : 'unreachable';
  });

  /** Nobody is loaded at all. Not the same as nobody matching, and said differently. */
  protected readonly isEmpty = computed(
    () => this.staff.hasValue() && this.everyone().length === 0,
  );

  /** There are people, and none of them survived the search or the role. */
  protected readonly nothingMatches = computed(
    () => this.everyone().length > 0 && this.rows().length === 0,
  );

  protected narrowTo(filter: RoleFilter): void {
    this.roleFilter.set(filter);
  }

  protected searchFor(event: Event): void {
    this.search.set((event.target as HTMLInputElement).value);
  }
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
