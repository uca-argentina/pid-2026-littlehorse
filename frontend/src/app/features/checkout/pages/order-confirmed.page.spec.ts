import { provideRouter } from '@angular/router';
import { render, screen } from '@testing-library/angular';
import { OrderConfirmedPage } from './order-confirmed.page';

async function openScreen(code = 'A-0000') {
  return render(OrderConfirmedPage, {
    inputs: { venueSlug: 'bar-alfa', code },
    providers: [provideRouter([])],
  });
}

describe('OrderConfirmedPage', () => {
  // The whole point of the screen: the code, big enough to read across a bar.
  it('shows the code of the order', async () => {
    await openScreen('K-4821');

    expect(screen.getByTestId('order-code').textContent).toContain('K-4821');
  });

  it('says the order is confirmed', async () => {
    await openScreen();

    expect(screen.getByRole('status').textContent).toMatch(/confirmado/i);
  });

  // US-12 is the screen this leads to and it does not exist yet. Drawn and
  // switched off, like "Ir a pagar" was before this one existed.
  it('cannot be followed yet', async () => {
    await openScreen();

    expect(screen.getByRole<HTMLButtonElement>('button', { name: /ver el estado/i }).disabled).toBe(
      true,
    );
  });

  it('offers the way back to the menu of this venue', async () => {
    await openScreen();

    expect(
      screen
        .getByText(/pedir algo más/i)
        .closest('a')
        ?.getAttribute('href'),
    ).toBe('/bar-alfa/menu');
  });

  // It survives a reload: the code is in the address, so somebody who locks
  // their phone and comes back still has what the bar will ask them for.
  it('takes the code from the address', async () => {
    await openScreen('Z-9999');

    expect(screen.getByTestId('order-code').textContent).toContain('Z-9999');
  });
});
