import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { fireEvent, render, screen } from '@testing-library/angular';
import { ProblemTypes } from '../../../core/api/problem-types';
import { Cart } from '../../../core/cart/cart';
import { STAMP_CARD_KEY_PREFIX, stampCardUrl } from '../../../core/stamp-card/stamp-cards';
import type { StampCardView } from '../../../core/stamp-card/stamp-cards';
import { BrowserStore } from '../../../core/storage/browser-store';
import { StoreInMemory } from '../../../core/storage/store-in-memory';
import { ordersUrl } from '../checkout.service';
import { PROCESSING_PAUSE_MS } from '../checkout.store';
import { CheckoutPage } from './checkout.page';

const TOKEN = 'c'.repeat(64);

const complete: StampCardView = {
  isActive: true,
  stampsForReward: 5,
  rewardProductId: 'id-2',
  rewardName: 'Fernet con Coca',
  stamps: 5,
  canRedeem: true,
};

let store: StoreInMemory;

async function openScreen(card: StampCardView) {
  store.write(STAMP_CARD_KEY_PREFIX + 'bar-alfa', TOKEN);

  const rendered = await render(CheckoutPage, {
    inputs: { venueSlug: 'bar-alfa' },
    providers: [
      provideRouter([]),
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: BrowserStore, useValue: store },
      { provide: PROCESSING_PAUSE_MS, useValue: 0 },
    ],
  });

  const cart = TestBed.inject(Cart);
  cart.open('bar-alfa');
  cart.add({ id: 'id-1', name: 'Gin Tonic', price: 4500 });

  // The shared card lives in the root injector: its own effect runs on this tick.
  TestBed.tick();
  await Promise.resolve();

  const http = TestBed.inject(HttpTestingController);
  http.expectOne((request) => request.url === stampCardUrl('bar-alfa')).flush(card);
  await rendered.fixture.whenStable();

  return { rendered, http };
}

function addReward(): void {
  fireEvent.click(screen.getByRole('button', { name: /agregar mi regalo/i }));
}

describe('CheckoutPage with a stamp card', () => {
  beforeEach(() => {
    store = new StoreInMemory();
  });

  // US-59, criterion 1.
  it('offers the reward when the card is complete', async () => {
    await openScreen(complete);

    expect(screen.getByTestId('reward-offer').textContent).toContain('Fernet con Coca');
    expect(screen.getByRole('button', { name: /agregar mi regalo/i })).toBeTruthy();
  });

  it('offers nothing while the card is not complete', async () => {
    await openScreen({ ...complete, stamps: 3, canRedeem: false });

    expect(screen.queryByTestId('reward-offer')).toBeNull();
  });

  it('offers nothing when the venue has the card switched off', async () => {
    await openScreen({
      isActive: false,
      stampsForReward: 0,
      rewardProductId: null,
      rewardName: null,
      stamps: 0,
      canRedeem: false,
    });

    expect(screen.queryByTestId('reward-offer')).toBeNull();
  });

  it('shows the reward at zero once it is added, and the total stays the same', async () => {
    const { rendered } = await openScreen(complete);

    addReward();
    await rendered.fixture.whenStable();

    const line = screen.getByTestId('reward-line');

    expect(line.textContent).toContain('Fernet con Coca');
    expect(line.textContent).toContain('Regalo');
    expect(line.textContent).toContain('$ 0,00');
    expect(screen.getByTestId('total').textContent).toContain('4.500,00');
  });

  it('can take the reward out again', async () => {
    const { rendered } = await openScreen(complete);

    addReward();
    await rendered.fixture.whenStable();
    fireEvent.click(screen.getByRole('button', { name: /quitar regalo/i }));
    await rendered.fixture.whenStable();

    expect(screen.queryByTestId('reward-line')).toBeNull();
    expect(screen.getByRole('button', { name: /agregar mi regalo/i })).toBeTruthy();
  });

  it('pays with the reward only when it was added', async () => {
    const { rendered, http } = await openScreen(complete);

    fireEvent.input(screen.getByLabelText<HTMLInputElement>(/nombre/i), {
      target: { value: 'María Quadro' },
    });
    addReward();
    await rendered.fixture.whenStable();
    screen.getByRole<HTMLButtonElement>('button', { name: /pagar/i }).click();
    await rendered.fixture.whenStable();

    const body = http.expectOne(ordersUrl('bar-alfa')).request.body;

    expect(body.redeemReward).toBe(true);
    expect(body.stampCardToken).toBe(TOKEN);
  });

  it('pays without the reward when it was not added', async () => {
    const { rendered, http } = await openScreen(complete);

    fireEvent.input(screen.getByLabelText<HTMLInputElement>(/nombre/i), {
      target: { value: 'María Quadro' },
    });
    await rendered.fixture.whenStable();
    screen.getByRole<HTMLButtonElement>('button', { name: /pagar/i }).click();
    await rendered.fixture.whenStable();

    expect(http.expectOne(ordersUrl('bar-alfa')).request.body.redeemReward).toBe(false);
  });

  // US-59, criterion 4: the other phone spent it first.
  it('says in Spanish that the card moved, drops the reward and reads the card again', async () => {
    const { rendered, http } = await openScreen(complete);

    fireEvent.input(screen.getByLabelText<HTMLInputElement>(/nombre/i), {
      target: { value: 'María Quadro' },
    });
    addReward();
    await rendered.fixture.whenStable();
    screen.getByRole<HTMLButtonElement>('button', { name: /pagar/i }).click();
    await rendered.fixture.whenStable();
    http
      .expectOne(ordersUrl('bar-alfa'))
      .flush({ type: ProblemTypes.stampCardCardMoved }, { status: 409, statusText: 'Conflict' });
    await new Promise((resolve) => setTimeout(resolve, 0));
    await new Promise((resolve) => setTimeout(resolve, 0));
    TestBed.tick();
    http
      .expectOne((request) => request.url === stampCardUrl('bar-alfa'))
      .flush({ ...complete, stamps: 0, canRedeem: false });
    await rendered.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toContain('otro celular');
    expect(screen.queryByTestId('reward-line')).toBeNull();

    expect(screen.queryByTestId('reward-offer')).toBeNull();
  });

  it('says it when the reward is not available any more, without blaming the order', async () => {
    const { rendered, http } = await openScreen(complete);

    fireEvent.input(screen.getByLabelText<HTMLInputElement>(/nombre/i), {
      target: { value: 'María Quadro' },
    });
    addReward();
    await rendered.fixture.whenStable();
    screen.getByRole<HTMLButtonElement>('button', { name: /pagar/i }).click();
    await rendered.fixture.whenStable();
    http
      .expectOne(ordersUrl('bar-alfa'))
      .flush(
        { type: ProblemTypes.stampCardRewardUnavailable },
        { status: 409, statusText: 'Conflict' },
      );
    await new Promise((resolve) => setTimeout(resolve, 0));
    await new Promise((resolve) => setTimeout(resolve, 0));
    TestBed.tick();
    http
      .expectOne((request) => request.url === stampCardUrl('bar-alfa'))
      .flush({ ...complete, stamps: 0, canRedeem: false });
    await rendered.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toContain('El regalo no está disponible');
    expect(screen.getByRole('alert').textContent).toContain('no se gastaron tus sellos');
  });
});
