import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { render, screen } from '@testing-library/angular';
import { stampCardUrl } from '../../../core/stamp-card/stamp-cards';
import type { StampCardView } from '../../../core/stamp-card/stamp-cards';
import { TrackingChannel } from '../tracking-channel';
import { trackingUrl } from '../tracking.service';
import type { TrackedOrder } from '../tracking.service';
import { TrackingStore } from '../tracking.store';
import { TrackingPage } from './tracking.page';

const token = '9f3c2ba7d81e4c06a1b2c3d4e5f60718';

const url = trackingUrl('bar-alfa', 'K-4821', token);

const card: StampCardView = {
  isActive: true,
  stampsForReward: 5,
  rewardProductId: 'id-2',
  rewardName: 'Fernet con Coca',
  stamps: 3,
  canRedeem: false,
};

const withAReward: TrackedOrder = {
  code: 'K-4821',
  customerName: 'María Quadro',
  status: 'Queued',
  total: 4500,
  savings: 0,
  paidAt: '2026-09-17T02:30:00Z',
  barName: null,
  items: [
    { productName: 'Gin Tonic', quantity: 1, note: null, isReward: false },
    { productName: 'Fernet con Coca', quantity: 1, note: null, isReward: true },
  ],
  tableNumber: null,
  waiterName: null,
};

async function openScreen(order: TrackedOrder, view: StampCardView) {
  const rendered = await render(TrackingPage, {
    inputs: { venueSlug: 'bar-alfa', code: 'K-4821', token },
    providers: [
      provideRouter([]),
      provideHttpClient(),
      provideHttpClientTesting(),
      {
        provide: TrackingChannel,
        useValue: {
          state: signal('connected' as const).asReadonly(),
          follow: () => undefined,
          disconnect: () => undefined,
        },
      },
    ],
  });

  TestBed.tick();
  await Promise.resolve();

  const http = TestBed.inject(HttpTestingController);
  http.expectOne(url).flush(order);
  http.expectOne((request) => request.url === stampCardUrl('bar-alfa')).flush(view);
  await rendered.fixture.whenStable();

  return { rendered, http, store: rendered.fixture.debugElement.injector.get(TrackingStore) };
}

describe('TrackingPage with a stamp card', () => {
  // US-58, criterion 2: "los veo en la carta y en el seguimiento".
  it('shows the stamps of the card', async () => {
    await openScreen(withAReward, card);

    expect(screen.getByTestId('stamps-progress').textContent).toContain('3 de 5 sellos');
  });

  it('shows no card when the venue has it switched off', async () => {
    await openScreen(withAReward, {
      isActive: false,
      stampsForReward: 0,
      rewardProductId: null,
      rewardName: null,
      stamps: 0,
      canRedeem: false,
    });

    expect(screen.queryByTestId('stamps-progress')).toBeNull();
  });

  // US-59: the reward is told apart from what was paid.
  it('marks the reward line as a gift and not as something paid', async () => {
    await openScreen(withAReward, card);

    expect(screen.getByTestId('reward-item').textContent).toContain('Fernet con Coca');
    expect(screen.getByTestId('reward-item').textContent).toContain('Regalo');
    expect(screen.getByTestId('paid-item').textContent).not.toContain('Regalo');
  });

  // The stamp is earned on delivery: the link dies then, and the card is read again.
  it('reads the card again when the order is handed over', async () => {
    const { rendered, http, store } = await openScreen(withAReward, card);

    store.askAgain();
    http.expectOne(url).flush({ type: 'x' }, { status: 404, statusText: 'Not Found' });
    TestBed.tick();
    await Promise.resolve();

    http
      .expectOne((request) => request.url === stampCardUrl('bar-alfa'))
      .flush({ ...card, stamps: 4 });
    await rendered.fixture.whenStable();

    expect(screen.getByTestId('stamps-progress').textContent).toContain('4 de 5 sellos');
  });
});
