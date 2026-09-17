import { Component, inject, input } from '@angular/core';
import { SignOutFlow } from '../../core/auth/sign-out-flow';

/**
 * Ends the shift, from any staff screen. Shared rather than repeated per
 * screen: a screen without a way out is a tablet somebody has to reload to
 * hand over.
 */
@Component({
  selector: 'drinkit-sign-out',
  styleUrl: './sign-out.scss',
  template: `<button class="ghost" type="button" (click)="leave()">Salir</button>`,
})
export class SignOut {
  private readonly signOut = inject(SignOutFlow);

  /** Where to land afterwards. The venue never comes from a screen. */
  readonly venueSlug = input.required<string>();

  protected leave(): void {
    this.signOut.leave(this.venueSlug());
  }
}
