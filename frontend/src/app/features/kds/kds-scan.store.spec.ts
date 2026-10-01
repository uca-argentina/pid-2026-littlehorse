import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SCAN_URL } from './kds-orders.service';
import type { ScannedOrder } from './kds-orders.service';
import { RECENT_SCANS_KEPT, SCAN_REPEAT_MS, KdsScanStore } from './kds-scan.store';

const token = '9f3c2ba7d81e4c06a1b2c3d4e5f60718';

const other = '0123456789abcdef0123456789abcdef';

function delivered(code = 'K-4821', customerName = 'María Quadro'): ScannedOrder {
  return { code, customerName, outcome: 'Delivered' };
}

function aStore(): { scans: KdsScanStore; http: HttpTestingController } {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      KdsScanStore,
      { provide: SCAN_REPEAT_MS, useValue: 3_000 },
    ],
  });

  return { scans: TestBed.inject(KdsScanStore), http: TestBed.inject(HttpTestingController) };
}

describe('KdsScanStore', () => {
  afterEach(() => vi.useRealTimers());

  it('sends what was read and records whose order it was', () => {
    const { scans, http } = aStore();

    scans.submit(token);
    const request = http.expectOne(SCAN_URL);
    request.flush(delivered());

    expect(request.request.body).toEqual({ read: token });
    expect(scans.recent()).toEqual([
      expect.objectContaining({ kind: 'found', code: 'K-4821', outcome: 'Delivered' }),
    ]);
  });

  // The token is the one secret the customer holds: it is sent, never kept.
  it('does not keep the token in what it shows', () => {
    const { scans, http } = aStore();

    scans.submit(token);
    http.expectOne(SCAN_URL).flush(delivered());

    expect(JSON.stringify(scans.recent())).not.toContain(token);
  });

  it('keeps the most recent scans, newest first', () => {
    const { scans, http } = aStore();

    for (let n = 0; n <= RECENT_SCANS_KEPT; n++) {
      scans.submit(n.toString(16).padStart(32, '0'));
      http.expectOne(SCAN_URL).flush(delivered(`K-000${n}`));
    }

    expect(scans.recent().map((scan) => (scan.kind === 'found' ? scan.code : ''))).toEqual([
      'K-0005',
      'K-0004',
      'K-0003',
      'K-0002',
      'K-0001',
    ]);
  });

  /**
   * The camera sees the same QR in every frame for as long as the phone is
   * held up. Sending each one would answer "ya se entregó" to the very scan
   * that just delivered it.
   */
  it('ignores the same code read again within a few seconds', () => {
    const { scans, http } = aStore();

    scans.submit(token);
    http.expectOne(SCAN_URL).flush(delivered());
    scans.submit(token);

    http.expectNone(SCAN_URL);
  });

  // US-19, criterion 4: somebody else holding up the same QR a while later.
  it('sends the same code again once the few seconds have passed', () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    const { scans, http } = aStore();

    scans.submit(token);
    http.expectOne(SCAN_URL).flush(delivered());
    vi.setSystemTime(Date.now() + 3_001);
    scans.submit(token);

    http.expectOne(SCAN_URL).flush({ ...delivered(), outcome: 'AlreadyDelivered' });
    expect(scans.recent()[0]).toEqual(expect.objectContaining({ outcome: 'AlreadyDelivered' }));
  });

  // The screen flashes for a scan that went out, not for a repeat frame.
  it('says whether it sent what was read', () => {
    const { scans, http } = aStore();

    expect(scans.submit(token)).toBe(true);
    http.expectOne(SCAN_URL).flush(delivered());
    expect(scans.submit(token)).toBe(false);
    expect(scans.submit('  ')).toBe(false);
  });

  // Two answers can land in the same millisecond; the list still tells them apart.
  it('gives every scan its own id', () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    const { scans, http } = aStore();
    const notFound = { status: 404, statusText: 'Not Found' };

    scans.submit(token);
    scans.submit(other);
    http.match(SCAN_URL).forEach((request) => request.flush({}, notFound));

    const [first, second] = scans.recent();
    expect(first!.id).not.toBe(second!.id);
  });

  it('does not send a different code as a repeat', () => {
    const { scans, http } = aStore();

    scans.submit(token);
    http.expectOne(SCAN_URL).flush(delivered());
    scans.submit(other);

    http.expectOne(SCAN_URL);
  });

  // A reader typing into a field ends with Enter, and sometimes a space.
  it('trims what the reader typed, and sends nothing for an empty read', () => {
    const { scans, http } = aStore();

    scans.submit('   ');
    http.expectNone(SCAN_URL);

    scans.submit(` ${token} `);
    expect(http.expectOne(SCAN_URL).request.body).toEqual({ read: token });
  });

  it('says it does not know a code the api finds no order for', () => {
    const { scans, http } = aStore();

    scans.submit(other);
    http
      .expectOne(SCAN_URL)
      .flush(
        { type: 'urn:drinkit:problem:kds:unknown-code' },
        { status: 404, statusText: 'Not Found' },
      );

    expect(scans.recent()[0]).toEqual(expect.objectContaining({ kind: 'unknown' }));
  });

  /**
   * The scan never reached the API, so nothing was delivered. Saying so, and
   * letting the same QR through again at once, is what keeps the customer from
   * waiting at the bar for a delivery that did not happen.
   */
  it('says the scan did not go through when the network fails, and lets it be read again', () => {
    const { scans, http } = aStore();

    scans.submit(token);
    http.expectOne(SCAN_URL).error(new ProgressEvent('error'));
    scans.submit(token);

    expect(scans.recent()[0]).toEqual(expect.objectContaining({ kind: 'failed' }));
    http.expectOne(SCAN_URL);
  });
});
