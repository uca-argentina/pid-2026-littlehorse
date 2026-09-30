import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { UNDO_OFFER_MS, KdsOrderActions } from './kds-order-actions';
import { deliverUrl, markReadyUrl, undoDeliveryUrl } from './kds-orders.service';

const noContent = { status: 204, statusText: 'No Content' };

function someActions(): {
  actions: KdsOrderActions;
  http: HttpTestingController;
  settled: string[];
} {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      KdsOrderActions,
      { provide: UNDO_OFFER_MS, useValue: 1_000 },
    ],
  });

  const actions = TestBed.inject(KdsOrderActions);
  const settled: string[] = [];
  actions.whenSettled((code) => settled.push(code));

  return { actions, http: TestBed.inject(HttpTestingController), settled };
}

describe('KdsOrderActions', () => {
  afterEach(() => vi.useRealTimers());

  // Its buttons stay pressed while the request is out: no second tap.
  it('holds an order busy until its request lands', () => {
    const { actions, http } = someActions();

    actions.markReady('K-4821');
    expect(actions.isBusy('K-4821')).toBe(true);

    http.expectOne(markReadyUrl('K-4821')).flush(null, noContent);
    expect(actions.isBusy('K-4821')).toBe(false);
  });

  it('tells the screen each time an action lands, whether it went through or not', () => {
    const { actions, http, settled } = someActions();

    actions.markReady('K-4821');
    http.expectOne(markReadyUrl('K-4821')).flush(null, noContent);
    actions.markReady('K-0066');
    http.expectOne(markReadyUrl('K-0066')).flush({}, { status: 400, statusText: 'Bad Request' });

    expect(settled).toEqual(['K-4821', 'K-0066']);
  });

  it('says which action on which order did not go through', () => {
    const { actions, http } = someActions();

    actions.deliver('K-4821');
    http.expectOne(deliverUrl('K-4821')).flush({}, { status: 400, statusText: 'Bad Request' });

    expect(actions.failure()).toEqual({ action: 'deliver', code: 'K-4821' });
  });

  it('forgets the last failure once another action starts', () => {
    const { actions, http } = someActions();

    actions.deliver('K-4821');
    http.expectOne(deliverUrl('K-4821')).flush({}, { status: 400, statusText: 'Bad Request' });
    actions.markReady('K-0066');

    expect(actions.failure()).toBeNull();
  });

  it('offers to undo a delivery for a few seconds', async () => {
    vi.useFakeTimers();
    const { actions, http } = someActions();

    actions.deliver('K-4821');
    http.expectOne(deliverUrl('K-4821')).flush(null, noContent);
    expect(actions.justDelivered()).toBe('K-4821');

    await vi.advanceTimersByTimeAsync(1_000);
    expect(actions.justDelivered()).toBeNull();
  });

  it('undoes a delivery and stops offering it', () => {
    const { actions, http } = someActions();

    actions.deliver('K-4821');
    http.expectOne(deliverUrl('K-4821')).flush(null, noContent);
    actions.undoDelivery('K-4821');

    expect(http.expectOne(undoDeliveryUrl('K-4821')).request.method).toBe('POST');
    expect(actions.justDelivered()).toBeNull();
  });
});
