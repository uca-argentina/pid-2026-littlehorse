import { Router, provideRouter } from '@angular/router';
import { render, screen } from '@testing-library/angular';
import { LastVenue } from '../../../core/venue/last-venue';
import { EntryPage } from './entry.page';

async function openTheApp(lastVenue: string | null) {
  return render(EntryPage, {
    providers: [
      provideRouter([{ path: ':venueSlug/staff/login', children: [] }]),
      { provide: LastVenue, useValue: { read: () => lastVenue, remember: () => undefined } },
    ],
  });
}

describe('EntryPage', () => {
  it('goes straight to the venue this device was last used at', async () => {
    const { fixture } = await openTheApp('bar-alfa');
    const router = fixture.debugElement.injector.get(Router);
    await fixture.whenStable();

    expect(router.url).toBe('/bar-alfa/staff/login');
  });

  it('explains how to get in when the device has never seen a venue', async () => {
    await openTheApp(null);

    expect(screen.getByRole('heading').textContent).toContain('drink.it');
    expect(screen.getByText(/Escaneá el QR del boliche/)).not.toBeNull();
  });
});
