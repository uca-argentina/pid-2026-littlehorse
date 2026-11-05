import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { BrowserStore } from '../storage/browser-store';
import { StoreInMemory } from '../storage/store-in-memory';
import { STAMP_CARD_HEADER, STAMP_CARD_KEY_PREFIX, StampCards, stampCardUrl } from './stamp-cards';
import type { StampCardView } from './stamp-cards';

const TOKEN = 'a'.repeat(64);

const view: StampCardView = {
  isActive: true,
  stampsForReward: 5,
  rewardProductId: 'id-1',
  rewardName: 'Gin Tonic',
  stamps: 3,
  canRedeem: false,
};

let store: StoreInMemory;

function aService(): { cards: StampCards; http: HttpTestingController } {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: BrowserStore, useValue: store },
    ],
  });

  return { cards: TestBed.inject(StampCards), http: TestBed.inject(HttpTestingController) };
}

async function settle(): Promise<void> {
  TestBed.tick();
  await Promise.resolve();
}

describe('StampCards', () => {
  beforeEach(() => {
    store = new StoreInMemory();
  });

  it('asks for the card of the venue that is open, with no token when the phone has none', async () => {
    const { cards, http } = aService();

    cards.open('bar-alfa');
    await settle();

    const asked = http.expectOne(stampCardUrl('bar-alfa'));

    expect(asked.request.headers.has(STAMP_CARD_HEADER)).toBe(false);
    asked.flush(view);
    await settle();

    expect(cards.view()).toEqual(view);
  });

  // US-58: the token is a secret, so it travels in a header and never in the address.
  it('sends the token it has kept in a header and not in the address', async () => {
    store.write(STAMP_CARD_KEY_PREFIX + 'bar-alfa', TOKEN);
    const { cards, http } = aService();

    cards.open('bar-alfa');
    await settle();

    const asked = http.expectOne((request) => request.url === stampCardUrl('bar-alfa'));

    expect(asked.request.headers.get(STAMP_CARD_HEADER)).toBe(TOKEN);
    expect(asked.request.urlWithParams).not.toContain(TOKEN);
    asked.flush(view);
  });

  it('keeps one token per venue', () => {
    const { cards } = aService();

    cards.open('bar-alfa');
    cards.remember('bar-alfa', TOKEN);

    expect(store.read(STAMP_CARD_KEY_PREFIX + 'bar-alfa')).toBe(TOKEN);
    expect(cards.tokenOf('bar-alfa')).toBe(TOKEN);
    expect(cards.tokenOf('bar-beta')).toBeNull();
  });

  it('asks again with the new token when the first order hands one over', async () => {
    const { cards, http } = aService();

    cards.open('bar-alfa');
    await settle();
    http.expectOne(stampCardUrl('bar-alfa')).flush({ ...view, stamps: 0 });
    await settle();

    cards.remember('bar-alfa', TOKEN);
    await settle();

    const again = http.expectOne((request) => request.url === stampCardUrl('bar-alfa'));

    expect(again.request.headers.get(STAMP_CARD_HEADER)).toBe(TOKEN);
    again.flush(view);
  });

  it('is hidden when the venue has its card switched off', async () => {
    const { cards, http } = aService();

    cards.open('bar-alfa');
    await settle();
    http
      .expectOne(stampCardUrl('bar-alfa'))
      .flush({ ...view, isActive: false, rewardProductId: null, rewardName: null, stamps: 0 });
    await settle();

    expect(cards.view()).toBeNull();
  });

  it('is nothing at all while the answer has not come', () => {
    const { cards } = aService();

    cards.open('bar-alfa');

    expect(cards.view()).toBeNull();
  });
});
