import { Component, inject } from '@angular/core';
import { Router } from '@angular/router';
import { LastVenue } from '../../../core/venue/last-venue';

/**
 * Where the app lands when the address carries no venue: the installed PWA
 * launching from the home screen, or a typo in the address bar.
 *
 * If this device has been used at a venue before, it goes straight there. Only
 * a device that has never opened a venue link sees the message, and there is
 * nothing else honest to show it: the venue comes from the QR, and the design
 * rules out a "choose your venue" screen.
 */
@Component({
  selector: 'drinkit-entry-page',
  styleUrl: './entry.page.scss',
  templateUrl: './entry.page.html',
})
export class EntryPage {
  private readonly router = inject(Router);

  constructor() {
    const slug = inject(LastVenue).read();

    if (slug !== null) void this.router.navigate([slug, 'staff', 'login']);
  }
}
