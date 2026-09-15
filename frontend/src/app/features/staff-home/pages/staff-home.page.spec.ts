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
    await openScreenAs('Kds');

    expect(screen.getByRole('heading').textContent).toContain('Hola, euge');
  });

  // The KDS and the waiter screens were cut from this sprint on purpose. Somebody
  // who signs in and sees nothing cannot tell a working system from a broken one.
  it('says a role has no screens yet instead of showing nothing', async () => {
    await openScreenAs('Kds');

    expect(screen.getByRole('status').textContent).toContain(
      'Todavía no hay pantallas para tu rol',
    );
  });

  it('shows the role in the language of the venue', async () => {
    await openScreenAs('Kds');

    expect(screen.getByText(/KDS · estación de barra/)).not.toBeNull();
  });

  it('shows the venue named in the address and not a fixed one', async () => {
    await openScreenAs('Kds');

    expect(screen.getByText(/bar-alfa/i)).not.toBeNull();
  });

  it('ends the session when the person signs out', async () => {
    const rendered = await openScreenAs('Kds');
    const sessions = rendered.fixture.debugElement.injector.get(SessionStorage);

    screen.getByRole('button', { name: /salir/i }).click();

    expect(sessions.hasSession()).toBe(false);
  });
});
