import { provideRouter } from '@angular/router';
import { render, screen } from '@testing-library/angular';
import { SessionStorage } from '../../core/auth/session-storage';
import { BrowserStore } from '../../core/storage/browser-store';
import { StoreInMemory } from '../../core/storage/store-in-memory';
import { StaffHeader } from './staff-header';

/**
 * The header every staff screen shares. The administration's own tabs are
 * covered by AdminHeader's spec; this is what changes for a screen that has
 * none — the till.
 */
async function openTheTillsHeader() {
  const rendered = await render(StaffHeader, {
    inputs: { venueSlug: 'bar-alfa', context: 'Caja' },
    providers: [provideRouter([]), { provide: BrowserStore, useValue: new StoreInMemory() }],
  });

  rendered.fixture.debugElement.injector.get(SessionStorage).remember({
    token: 'un-token',
    expiresAt: new Date(Date.now() + 60_000).toISOString(),
    username: 'laura.caja',
    role: 'Cashier',
  });
  await rendered.fixture.whenStable();
}

describe('StaffHeader', () => {
  it('says which screen of the venue this is', async () => {
    await openTheTillsHeader();

    expect(screen.getByText('Caja')).not.toBeNull();
  });

  it('shows the account as a chip with the username and the role', async () => {
    await openTheTillsHeader();

    expect(screen.getByRole('button', { name: /laura\.caja · cajero/i })).not.toBeNull();
  });

  // A menu button that opens nothing is a control that lies.
  it('has no navigation and no menu button when the screen has no sections', async () => {
    await openTheTillsHeader();

    expect(screen.queryByRole('navigation')).toBeNull();
    expect(screen.queryByLabelText(/menú/i)).toBeNull();
  });
});
