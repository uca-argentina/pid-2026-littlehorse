import { render, screen } from '@testing-library/angular';
import { BrowserStore } from '../../core/storage/browser-store';
import { StoreInMemory } from '../../core/storage/store-in-memory';
import { ThemeToggle } from './theme-toggle';

const STORAGE_KEY = 'drinkit.theme';

// A fresh StoreInMemory per test keeps a choice in one case out of the next.
let store: StoreInMemory;

async function openToggle() {
  return render(ThemeToggle, { providers: [{ provide: BrowserStore, useValue: store }] });
}

describe('ThemeToggle', () => {
  beforeEach(() => {
    store = new StoreInMemory();
    document.documentElement.removeAttribute('data-theme');
  });

  // Dark is always where the shift starts: the device's own preference is
  // never read, only what somebody chose here last time.
  it('starts on the dark theme, offering to switch to light', async () => {
    await openToggle();

    const toggle = screen.getByRole('button', { name: /cambiar a tema claro/i });

    expect(document.documentElement.getAttribute('data-theme')).toBe('dark');
    expect(toggle.getAttribute('aria-pressed')).toBe('false');
  });

  it('switches to the light theme and back, and remembers the choice', async () => {
    const rendered = await openToggle();

    screen.getByRole('button', { name: /cambiar a tema claro/i }).click();
    await rendered.fixture.whenStable();

    expect(document.documentElement.getAttribute('data-theme')).toBe('light');
    expect(
      screen.getByRole('button', { name: /cambiar a tema oscuro/i }).getAttribute('aria-pressed'),
    ).toBe('true');
    expect(store.entries.get(STORAGE_KEY)).toBe('light');

    screen.getByRole('button', { name: /cambiar a tema oscuro/i }).click();
    await rendered.fixture.whenStable();

    expect(document.documentElement.getAttribute('data-theme')).toBe('dark');
  });
});
