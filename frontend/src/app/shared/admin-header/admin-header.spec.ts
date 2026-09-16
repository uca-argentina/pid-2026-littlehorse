import { TestBed } from '@angular/core/testing';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { fireEvent, render, screen } from '@testing-library/angular';
import { SessionStorage } from '../../core/auth/session-storage';
import { BrowserStore } from '../../core/storage/browser-store';
import { StoreInMemory } from '../../core/storage/store-in-memory';
import { AdminHeader } from './admin-header';

// A fresh StoreInMemory per test keeps a choice in one case out of the next.
let store: StoreInMemory;

async function openHeader() {
  const rendered = await render(AdminHeader, {
    inputs: { venueSlug: 'bar-alfa' },
    // The tabs and Salir navigate for real: without a route to land on, the
    // router rejects and that masks the actual assertion.
    providers: [
      provideRouter([
        { path: ':venueSlug/staff/login', children: [] },
        { path: ':venueSlug/staff/products', children: [] },
      ]),
      { provide: BrowserStore, useValue: store },
    ],
  });

  const sessions = rendered.fixture.debugElement.injector.get(SessionStorage);
  sessions.remember({
    token: 'un-token',
    expiresAt: new Date(Date.now() + 60_000).toISOString(),
    username: 'euge',
    role: 'Administrator',
  });
  await rendered.fixture.whenStable();

  return { rendered, sessions };
}

function accountChip(): HTMLButtonElement {
  return screen.getByRole('button', { name: /euge · administrador/i }) as HTMLButtonElement;
}

// By its label and not by its role: the stylesheet only draws the button on a
// phone, this runs at no width at all, and a hidden element has no accessible
// name to query by. What it says about the navigation is checked either way.
function menuToggle(): HTMLButtonElement {
  return screen.getByLabelText(/menú/i) as HTMLButtonElement;
}

describe('AdminHeader', () => {
  // Isolates each test from whatever an earlier one chose, and from a real
  // browser's own leftover choice: the device's light/dark setting is never
  // read, only this.
  beforeEach(() => {
    store = new StoreInMemory();
    document.documentElement.removeAttribute('data-theme');
  });

  // The two administration screens, reachable from each other: until this
  // existed, an administrator on one of them had no way to the other but the
  // address bar.
  it('offers a tab for each administration screen', async () => {
    await openHeader();

    expect(screen.getByRole('link', { name: /productos/i }).getAttribute('href')).toBe(
      '/bar-alfa/staff/products',
    );
    expect(screen.getByRole('link', { name: /staff/i }).getAttribute('href')).toBe(
      '/bar-alfa/staff/users',
    );
  });

  // Marked for a screen reader as well as by colour: the tab you are on is
  // the one that says so.
  it('marks the tab of the screen that is open', async () => {
    TestBed.configureTestingModule({
      providers: [
        // As in app.config.ts: it is how venueSlug reaches the component.
        provideRouter(
          [
            { path: ':venueSlug/staff/products', component: AdminHeader },
            { path: ':venueSlug/staff/users', component: AdminHeader },
          ],
          withComponentInputBinding(),
        ),
      ],
    });
    const harness = await RouterTestingHarness.create();

    await harness.navigateByUrl('/bar-alfa/staff/users');
    await harness.fixture.whenStable();

    expect(screen.getByRole('link', { name: /staff/i }).getAttribute('aria-current')).toBe('page');
    expect(
      screen.getByRole('link', { name: /productos/i }).getAttribute('aria-current'),
    ).toBeNull();
  });

  // Whose account this is and what it can do, in the venue's words: a shared
  // laptop never hides who is signed in.
  it('shows the account as a chip with the username and the role', async () => {
    await openHeader();

    expect(accountChip().getAttribute('aria-expanded')).toBe('false');
  });

  it('keeps the way out behind the chip until it is asked for', async () => {
    await openHeader();

    expect(screen.queryByRole('menuitem', { name: /salir/i })).toBeNull();
  });

  it('opens the account menu with the way out of the shift', async () => {
    const { rendered } = await openHeader();

    accountChip().click();
    await rendered.fixture.whenStable();

    expect(accountChip().getAttribute('aria-expanded')).toBe('true');
    expect(screen.getByRole('menuitem', { name: /salir/i })).not.toBeNull();
  });

  it('ends the session from that menu', async () => {
    const { rendered, sessions } = await openHeader();

    accountChip().click();
    await rendered.fixture.whenStable();
    screen.getByRole('menuitem', { name: /salir/i }).click();

    expect(sessions.hasSession()).toBe(false);
  });

  it('closes the account menu with Escape', async () => {
    const { rendered } = await openHeader();

    accountChip().click();
    await rendered.fixture.whenStable();
    fireEvent.keyDown(document, { key: 'Escape' });
    await rendered.fixture.whenStable();

    expect(screen.queryByRole('menuitem', { name: /salir/i })).toBeNull();
  });

  // On a phone the tabs fold into a menu behind one button. Whether they are
  // drawn is the stylesheet's business; what the button says about them is
  // what a screen reader, and this test, can check.
  it('has a menu button that reports whether the navigation is open', async () => {
    const { rendered } = await openHeader();

    expect(menuToggle().getAttribute('aria-expanded')).toBe('false');
    expect(menuToggle().getAttribute('aria-controls')).toBe(
      screen.getByRole('navigation', { hidden: true }).id,
    );

    menuToggle().click();
    await rendered.fixture.whenStable();

    expect(menuToggle().getAttribute('aria-expanded')).toBe('true');
  });

  // Picking a screen is the end of the menu's job: leaving it open would
  // cover the top of the screen that was just asked for.
  it('closes the navigation after a tab is picked', async () => {
    const { rendered } = await openHeader();

    menuToggle().click();
    await rendered.fixture.whenStable();
    screen.getByRole('link', { name: /productos/i }).click();
    await rendered.fixture.whenStable();

    expect(menuToggle().getAttribute('aria-expanded')).toBe('false');
  });

  // The toggle's own behaviour — default, switching, persisting — is
  // ThemeToggle's spec. This only checks the header actually carries one.
  it('offers a way to switch themes', async () => {
    await openHeader();

    expect(screen.getByRole('button', { name: /cambiar a tema claro/i })).not.toBeNull();
  });
});
