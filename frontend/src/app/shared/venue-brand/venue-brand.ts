import { Component, input } from '@angular/core';

/**
 * The venue's mark, shown at the top of every staff screen. It is the only
 * confirmation on screen of which venue you are working in, so it must never
 * show a name that was not resolved from the address.
 */
@Component({
  selector: 'drinkit-venue-brand',
  styleUrl: './venue-brand.scss',
  templateUrl: './venue-brand.html',
})
export class VenueBrand {
  /**
   * What identifies the venue on screen. The slug for now: it comes from the
   * address the person opened, so it is always true. The display name would
   * need the API to hand it over, and the login screen is drawn before there
   * is any session to ask with.
   */
  readonly venue = input.required<string>();
}
