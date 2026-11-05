import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { render, screen } from '@testing-library/angular';
import {
  STAMP_CARD_HEADER,
  STAMP_CARD_KEY_PREFIX,
  stampCardUrl,
} from '../../../core/stamp-card/stamp-cards';
import type { StampCardView } from '../../../core/stamp-card/stamp-cards';
import { BrowserStore } from '../../../core/storage/browser-store';
import { StoreInMemory } from '../../../core/storage/store-in-memory';
import { OrdersInProgressChannel } from '../components/orders-in-progress-channel';
import { menuUrl } from '../menu.service';
import type { Menu } from '../menu.service';
import { MenuPage } from './menu.page';

const TOKEN = 'd'.repeat(64);

const menu: Menu = { venueName: 'Bar Alfa', isTakingOrders: true, categories: [], items: [] };

const card: StampCardView = {
  isActive: true,
  stampsForReward: 5,
  rewardProductId: 'id-1',
  rewardName: 'Gin Tonic',
  stamps: 3,
  canRedeem: false,
};

let store: StoreInMemory;

async function openScreen(view: StampCardView) {
  const rendered = await render(MenuPage, {
    inputs: { venueSlug: 'bar-alfa' },
    providers: [
      provideRouter([]),
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: BrowserStore, useValue: store },
      {
        provide: OrdersInProgressChannel,
        useValue: { follow: () => undefined, disconnect: () => undefined },
      },
    ],
  });

  TestBed.tick();
  await Promise.resolve();

  const http = TestBed.inject(HttpTestingController);
  http.expectOne(menuUrl('bar-alfa')).flush(menu);
  const asked = http.expectOne((request) => request.url === stampCardUrl('bar-alfa'));
  asked.flush(view);
  await rendered.fixture.whenStable();

  return { rendered, asked };
}

describe('MenuPage with a stamp card', () => {
  beforeEach(() => {
    store = new StoreInMemory();
  });

  // US-58, criterion 2: the stamps are on the menu.
  it('shows the stamps out of what the reward takes, and which reward it is', async () => {
    await openScreen(card);

    expect(screen.getByTestId('stamps-progress').textContent).toContain('3 de 5 sellos');
    expect(screen.getByText(/te regalamos un Gin Tonic/)).toBeTruthy();
  });

  // US-57, criterion 3: switched off, the customer does not see it.
  it('shows no card when the venue has it switched off', async () => {
    await openScreen({
      isActive: false,
      stampsForReward: 0,
      rewardProductId: null,
      rewardName: null,
      stamps: 0,
      canRedeem: false,
    });

    expect(screen.queryByTestId('stamps-progress')).toBeNull();
    expect(screen.queryByText(/tarjeta de sellos/i)).toBeNull();
  });

  it('says the reward is ready when the card is complete', async () => {
    await openScreen({ ...card, stamps: 5, canRedeem: true });

    expect(screen.getByTestId('stamps-ready').textContent).toContain('Gin Tonic');
  });

  it('asks with the token the phone keeps, in a header', async () => {
    store.write(STAMP_CARD_KEY_PREFIX + 'bar-alfa', TOKEN);

    const { asked } = await openScreen(card);

    expect(asked.request.headers.get(STAMP_CARD_HEADER)).toBe(TOKEN);
  });
});
