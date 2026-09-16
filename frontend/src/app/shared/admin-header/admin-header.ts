import { Component, ElementRef, computed, inject, input, signal } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { SessionStorage } from '../../core/auth/session-storage';
import { SignOutFlow } from '../../core/auth/sign-out-flow';
import { staffRoleName } from '../../core/staff/staff-roles';
import { VenueBrand } from '../venue-brand/venue-brand';

/**
 * The top of every administration screen: the venue, whose account this is,
 * and a tab per screen underneath. Shared so the screens are reachable from
 * each other — until this existed, an administrator on one of them had no
 * way to the other but the address bar.
 *
 * Two things fold away. The account chip opens a menu with the way out of the
 * shift, so the header carries no button that competes with the screen's own
 * action. And on a phone the tabs hide behind a menu button: the stylesheet
 * decides when, this class only says whether it is open.
 */
@Component({
  selector: 'drinkit-admin-header',
  imports: [RouterLink, RouterLinkActive, VenueBrand],
  styleUrl: './admin-header.scss',
  templateUrl: './admin-header.html',
  host: {
    '(document:keydown.escape)': 'closeEverything()',
    '(document:click)': 'closeIfOutside($event)',
  },
})
export class AdminHeader {
  private readonly sessions = inject(SessionStorage);

  private readonly signOut = inject(SignOutFlow);

  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef);

  /** From the path. Bound by the router, so the header never asks for a venue. */
  readonly venueSlug = input.required<string>();

  protected readonly username = computed(() => this.sessions.session()?.username ?? '');

  protected readonly roleName = computed(() => staffRoleName(this.sessions.session()?.role ?? ''));

  protected readonly accountOpen = signal(false);

  protected readonly navigationOpen = signal(false);

  protected toggleAccount(): void {
    this.accountOpen.update((open) => !open);
    this.navigationOpen.set(false);
  }

  protected toggleNavigation(): void {
    this.navigationOpen.update((open) => !open);
    this.accountOpen.set(false);
  }

  /** Picking a screen is the end of the menu's job. */
  protected closeNavigation(): void {
    this.navigationOpen.set(false);
  }

  protected closeEverything(): void {
    this.accountOpen.set(false);
    this.navigationOpen.set(false);
  }

  /** A tap anywhere else on the screen means "not that", on a menu as on a dialog. */
  protected closeIfOutside(event: Event): void {
    if (event.target instanceof Node && this.element.nativeElement.contains(event.target)) return;

    this.closeEverything();
  }

  protected leave(): void {
    this.signOut.leave(this.venueSlug());
  }
}
