import { provideRouter } from '@angular/router';
import { render, screen } from '@testing-library/angular';
import { SessionStorage } from '../../../core/auth/session-storage';
import type { StaffSession } from '../../../core/auth/staff-session';
import { StaffHomePage } from './staff-home.page';

function sessionFor(role: string): StaffSession {
  return {
    token: 'un-token',
    expiresAt: new Date(Date.now() + 60_000).toISOString(),
    username: 'euge',
    role,
  };
}

async function openScreenAs(role: string) {
  const rendered = await render(StaffHomePage, {
    inputs: { venueSlug: 'bar-alfa' },
    // Salir navigates for real: without a route to land on, the router rejects
    // and the rejection surfaces as an unhandled error that can mask a failure.
    providers: [provideRouter([{ path: ':venueSlug/staff/login', children: [] }])],
  });

  rendered.fixture.debugElement.injector.get(SessionStorage).remember(sessionFor(role));
  await rendered.fixture.whenStable();

  return rendered;
}

describe('StaffHomePage', () => {
  it('greets whoever signed in by name', async () => {
    await openScreenAs('Administrator');

    expect(screen.getByRole('heading').textContent).toContain('Hola, euge');
  });

  it('says a role has no screens yet instead of showing nothing', async () => {
    await openScreenAs('Bartender');

    expect(screen.getByRole('status').textContent).toContain(
      'Todavía no hay pantallas para tu rol',
    );
  });

  it('shows the role in the language of the venue', async () => {
    await openScreenAs('Bartender');

    expect(screen.getByText(/KDS · estación de barra/)).not.toBeNull();
  });

  it('shows the venue named in the address and not a fixed one', async () => {
    await openScreenAs('Administrator');

    expect(screen.getByText(/bar-alfa/i)).not.toBeNull();
  });

  it('ends the session when the person signs out', async () => {
    const rendered = await openScreenAs('Administrator');
    const sessions = rendered.fixture.debugElement.injector.get(SessionStorage);

    screen.getByRole('button', { name: /salir/i }).click();

    expect(sessions.hasSession()).toBe(false);
  });
});
