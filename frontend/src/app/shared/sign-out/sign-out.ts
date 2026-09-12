import { Component, inject, input } from '@angular/core';
import { Router } from '@angular/router';
import { SessionStorage } from '../../core/auth/session-storage';

/**
 * Ends the shift, from any staff screen. Shared rather than repeated per
 * screen: forgetting the session and leaving for the login are two steps that
 * must not come apart, and a screen without a way out is a tablet somebody has
 * to reload to hand over.
 */
@Component({
  selector: 'drinkit-sign-out',
  styleUrl: './sign-out.scss',
  template: `<button class="ghost" type="button" (click)="leave()">Salir</button>`,
})
export class SignOut {
  private readonly sessions = inject(SessionStorage);

  private readonly router = inject(Router);

  /** Where to land afterwards. The venue never comes from a screen. */
  readonly venueSlug = input.required<string>();

  protected leave(): void {
    this.sessions.forget();
    void this.router.navigate([this.venueSlug(), 'staff', 'login']);
  }
}
