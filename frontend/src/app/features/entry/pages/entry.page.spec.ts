import { Router, provideRouter } from '@angular/router';
import { render, screen } from '@testing-library/angular';
import { LastVenue } from '../../../core/venue/last-venue';
import { EntryPage } from './entry.page';

async function openTheApp(lastVenue: string | null) {
  return render(EntryPage, {
    providers: [
      provideRouter([{ path: ':venueSlug/personal/ingresar', children: [] }]),
      { provide: LastVenue, useValue: { read: () => lastVenue, remember: () => undefined } },
    ],
  });
}

describe('EntryPage', () => {
  it('Open_WhenThisDeviceWasUsedAtAVenue_GoesStraightThere', async () => {
    const { fixture } = await openTheApp('bar-alfa');
    const router = fixture.debugElement.injector.get(Router);
    await fixture.whenStable();

    expect(router.url).toBe('/bar-alfa/personal/ingresar');
  });

  it('Open_WhenTheDeviceHasNeverSeenAVenue_ExplainsHowToGetIn', async () => {
    await openTheApp(null);

    expect(screen.getByRole('heading').textContent).toContain('drink.it');
    expect(screen.getByText(/Escaneá el QR del boliche/)).not.toBeNull();
  });
});
