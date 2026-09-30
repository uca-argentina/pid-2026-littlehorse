import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { fireEvent, render, screen, within } from '@testing-library/angular';
import { KdsBoardChannel } from '../../../core/kds/kds-board-channel';
import type { KdsLinkState } from '../../../core/kds/kds-board-channel';
import { KdsCamera, OPEN_CAMERA } from '../components/kds-camera';
import { SCAN_URL, deliverUrl, markReadyUrl, undoDeliveryUrl } from '../kds-orders.service';
import type { ScannedOrder } from '../kds-orders.service';
import { KDS_QUEUE_URL } from '../kds-queue';
import type { KdsQueueOrder } from '../kds-queue';
import { CAMERA_IDLE_MS, SCAN_FLASH_MS, KdsScanPage } from './kds-scan.page';

const token = '9f3c2ba7d81e4c06a1b2c3d4e5f60718';

const noContent = { status: 204, statusText: 'No Content' };

class FakeKdsBoardChannel {
  readonly state = signal<KdsLinkState>('connected');

  connect(): void {
    // Nothing to hear in these tests: every answer is handed over by hand.
  }

  disconnect(): void {
    // Nothing was opened.
  }
}

function anOrder(overrides: Partial<KdsQueueOrder> = {}): KdsQueueOrder {
  return {
    code: 'K-4821',
    customerName: 'María Quadro',
    status: 'InPreparation',
    paidAt: new Date().toISOString(),
    lastModifiedAt: null,
    isForTable: false,
    orderItems: [{ productName: 'Gin Tonic', quantity: 2, note: null }],
    ...overrides,
  };
}

function scanned(outcome: ScannedOrder['outcome']): ScannedOrder {
  return { code: 'K-4821', customerName: 'María Quadro', outcome };
}

function signedInAsTheStation(): void {
  sessionStorage.setItem(
    'drinkit.staff-session',
    JSON.stringify({
      token: 'un-token',
      expiresAt: new Date(Date.now() + 60_000).toISOString(),
      username: 'barra.demo',
      role: 'Kds',
    }),
  );
}

afterEach(() => sessionStorage.clear());

async function openScreenWith(queue: KdsQueueOrder[] = []) {
  signedInAsTheStation();

  // No camera in a test: asking for it fails, the way a tablet without one does.
  const cameraAskedFor = vi.fn(() => Promise.reject(new DOMException('none', 'NotFoundError')));

  const rendered = await render(KdsScanPage, {
    inputs: { venueSlug: 'bar-alfa' },
    providers: [
      provideRouter([]),
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: KdsBoardChannel, useValue: new FakeKdsBoardChannel() },
      { provide: OPEN_CAMERA, useValue: cameraAskedFor },
      { provide: CAMERA_IDLE_MS, useValue: 1_000 },
      { provide: SCAN_FLASH_MS, useValue: 500 },
    ],
  });

  const http = TestBed.inject(HttpTestingController);
  http.expectOne(KDS_QUEUE_URL).flush(queue);
  await rendered.fixture.whenStable();

  return { rendered, http, cameraAskedFor };
}

/** The camera, when it is open: what it reads is handed over as the component would. */
function theCamera(rendered: Awaited<ReturnType<typeof openScreenWith>>['rendered']) {
  return rendered.fixture.debugElement.query(By.directive(KdsCamera));
}

function theScanBlock(): HTMLElement {
  return screen.getByRole('region', { name: 'Escaneá el QR del cliente' });
}

/** What a reader plugged into the tablet does: types the code and presses Enter. */
function theReaderTypes(read: string): void {
  const field = screen.getByRole('textbox', { name: 'Lectura del lector' });

  fireEvent.input(field, { target: { value: read } });
  fireEvent.keyDown(field, { key: 'Enter' });
}

async function theScanAnswers(
  rendered: Awaited<ReturnType<typeof openScreenWith>>['rendered'],
  http: HttpTestingController,
  answer: ScannedOrder,
) {
  http.expectOne(SCAN_URL).flush(answer);
  await rendered.fixture.whenStable();
}

function lastScan(): HTMLElement {
  return screen.getByRole('status', { name: 'Último escaneo' });
}

describe('KdsScanPage', () => {
  // A reader types into whatever has the focus: arriving here, that is its field.
  it('opens with the reader field ready to receive a code', async () => {
    await openScreenWith();

    expect(document.activeElement).toBe(
      screen.getByRole('textbox', { name: 'Lectura del lector' }),
    );
  });

  it('sends what the reader types when it presses enter', async () => {
    const { http } = await openScreenWith();

    theReaderTypes(token);

    expect(http.expectOne(SCAN_URL).request.body).toEqual({ read: token });
  });

  // A reader types into the field, and the next code must not land after it.
  it('clears the reader field after each read', async () => {
    const { http } = await openScreenWith();

    theReaderTypes(token);
    http.expectOne(SCAN_URL);

    expect(
      (screen.getByRole('textbox', { name: 'Lectura del lector' }) as HTMLInputElement).value,
    ).toBe('');
  });

  // US-19, criterion 1.
  it('says the order was handed over, and whose it was', async () => {
    const { rendered, http } = await openScreenWith();

    theReaderTypes(token);
    await theScanAnswers(rendered, http, scanned('Delivered'));

    expect(lastScan().textContent).toMatch(/entregado/i);
    expect(lastScan().textContent).toContain('K-4821');
    expect(lastScan().textContent).toContain('María Quadro');
  });

  // US-19, criterion 3.
  it('warns that an order is not ready yet', async () => {
    const { rendered, http } = await openScreenWith();

    theReaderTypes(token);
    await theScanAnswers(rendered, http, scanned('NotReadyYet'));

    expect(lastScan().textContent).toMatch(/todav[íi]a no est[áa] listo/i);
  });

  // US-19, criterion 4: two people claiming the same order.
  it('warns that an order was already handed over', async () => {
    const { rendered, http } = await openScreenWith();

    theReaderTypes(token);
    await theScanAnswers(rendered, http, scanned('AlreadyDelivered'));

    expect(lastScan().textContent).toMatch(/ya se entreg[óo]/i);
  });

  // A mistaken scan is undone the same way as a mistaken tap on "Entregado".
  it('offers to undo a delivery made by scan', async () => {
    const { rendered, http } = await openScreenWith();

    theReaderTypes(token);
    await theScanAnswers(rendered, http, scanned('Delivered'));
    fireEvent.click(screen.getByRole('button', { name: 'Deshacer' }));

    expect(http.expectOne(undoDeliveryUrl('K-4821')).request.method).toBe('POST');
  });

  it('offers no undo when the scan handed nothing over', async () => {
    const { rendered, http } = await openScreenWith();

    theReaderTypes(token);
    await theScanAnswers(rendered, http, scanned('NotReadyYet'));

    expect(screen.queryByRole('button', { name: 'Deshacer' })).toBeNull();
  });

  /**
   * The answer never came back, which does not mean nothing happened: the
   * order may be delivered already. Saying "sin conexión" and nothing else
   * sends the bartender to scan again, read "ya se entregó" and turn away the
   * customer it was delivered to.
   */
  it('is honest about a scan whose answer never arrived', async () => {
    const { rendered, http } = await openScreenWith();

    theReaderTypes(token);
    http.expectOne(SCAN_URL).error(new ProgressEvent('error'));
    await rendered.fixture.whenStable();

    expect(lastScan().textContent).toMatch(/no sabemos si se registr[óo]/i);
    expect(lastScan().textContent).toMatch(/ya se entreg[óo].*fue este/i);
  });

  it('says it does not know a code of no order here', async () => {
    const { rendered, http } = await openScreenWith();

    theReaderTypes(token);
    http
      .expectOne(SCAN_URL)
      .flush(
        { type: 'urn:drinkit:problem:kds:unknown-code' },
        { status: 404, statusText: 'Not Found' },
      );
    await rendered.fixture.whenStable();

    expect(lastScan().textContent).toMatch(/no reconocemos/i);
  });

  it('lists the last scans, newest first', async () => {
    const { rendered, http } = await openScreenWith();

    theReaderTypes(token);
    await theScanAnswers(rendered, http, scanned('Delivered'));
    theReaderTypes('0123456789abcdef0123456789abcdef');
    await theScanAnswers(rendered, http, {
      code: 'K-0066',
      customerName: 'Pablo',
      outcome: 'NotReadyYet',
    });

    const rows = within(screen.getByRole('list', { name: 'Últimos escaneos' })).getAllByRole(
      'listitem',
    );
    expect(rows.map((row) => row.textContent)).toEqual([
      expect.stringContaining('K-0066'),
      expect.stringContaining('K-4821'),
    ]);
  });

  describe('the camera', () => {
    // Like KdsEscanear: the block waits for a reader, and the camera is asked for.
    it('stays closed until somebody asks for it', async () => {
      const { rendered, cameraAskedFor } = await openScreenWith();

      expect(theCamera(rendered)).toBeNull();
      expect(cameraAskedFor).not.toHaveBeenCalled();
    });

    it('opens when asked for', async () => {
      const { rendered } = await openScreenWith();

      fireEvent.click(screen.getByRole('button', { name: 'Usar la cámara' }));
      await rendered.fixture.whenStable();

      expect(theCamera(rendered)).not.toBeNull();
    });

    // One customer at a time: once it has read a code it has done its job.
    it('closes once it has read a code, and sends it', async () => {
      const { rendered, http } = await openScreenWith();

      fireEvent.click(screen.getByRole('button', { name: 'Usar la cámara' }));
      await rendered.fixture.whenStable();
      (theCamera(rendered).componentInstance as KdsCamera).read.emit(token);
      await rendered.fixture.whenStable();

      expect(http.expectOne(SCAN_URL).request.body).toEqual({ read: token });
      expect(theCamera(rendered)).toBeNull();
      expect(document.activeElement).toBe(
        screen.getByRole('textbox', { name: 'Lectura del lector' }),
      );
    });

    it('closes when cancelled', async () => {
      const { rendered } = await openScreenWith();

      fireEvent.click(screen.getByRole('button', { name: 'Usar la cámara' }));
      await rendered.fixture.whenStable();
      fireEvent.click(screen.getByRole('button', { name: 'Cancelar' }));
      await rendered.fixture.whenStable();

      expect(theCamera(rendered)).toBeNull();
    });

    // Left open with nobody in front of it, it would keep its light on all night.
    it('closes on its own when it reads nothing for a while', async () => {
      const { rendered } = await openScreenWith();
      vi.useFakeTimers();

      fireEvent.click(screen.getByRole('button', { name: 'Usar la cámara' }));
      await vi.advanceTimersByTimeAsync(1_000);
      rendered.fixture.detectChanges();

      expect(theCamera(rendered)).toBeNull();
      vi.useRealTimers();
    });
  });

  describe('the flash', () => {
    // Read from the corner of an eye: something was read and sent.
    it('flashes the scan block when a code is sent', async () => {
      const { rendered, http } = await openScreenWith();

      theReaderTypes(token);
      http.expectOne(SCAN_URL);
      rendered.fixture.detectChanges();

      expect(theScanBlock().getAttribute('data-flash')).toBe('true');
    });

    it('stops flashing a moment later', async () => {
      const { rendered, http } = await openScreenWith();
      vi.useFakeTimers();

      theReaderTypes(token);
      http.expectOne(SCAN_URL);
      await vi.advanceTimersByTimeAsync(500);
      rendered.fixture.detectChanges();

      expect(theScanBlock().getAttribute('data-flash')).toBe('false');
      vi.useRealTimers();
    });

    // The camera sees the same QR frame after frame: those are not new scans.
    it('does not flash for a repeat that is not sent', async () => {
      const { rendered, http } = await openScreenWith();
      vi.useFakeTimers();

      theReaderTypes(token);
      http.expectOne(SCAN_URL).flush(scanned('Delivered'));
      await vi.advanceTimersByTimeAsync(500);
      theReaderTypes(token);
      rendered.fixture.detectChanges();

      expect(theScanBlock().getAttribute('data-flash')).toBe('false');
      vi.useRealTimers();
    });
  });

  it('offers the way back to the board', async () => {
    await openScreenWith();

    expect(screen.getByRole('link', { name: 'Volver al tablero' }).getAttribute('href')).toBe(
      '/bar-alfa/staff/kds',
    );
  });

  describe('searching by hand', () => {
    function search(query: string): void {
      fireEvent.input(screen.getByRole('searchbox', { name: 'Buscar pedido' }), {
        target: { value: query },
      });
    }

    /**
     * After a search the focus stays in the searchbox, and a reader types
     * wherever the focus is. What it types is a scan, not a search: it goes out
     * as one, and the customer's secret does not sit on screen.
     */
    it('sends a code the reader typed into the search as a scan', async () => {
      const { rendered, http } = await openScreenWith();
      const searchbox = screen.getByRole('searchbox', { name: 'Buscar pedido' });

      fireEvent.input(searchbox, { target: { value: token } });
      fireEvent.keyDown(searchbox, { key: 'Enter' });
      await rendered.fixture.whenStable();

      expect(http.expectOne(SCAN_URL).request.body).toEqual({ read: token });
      expect((searchbox as HTMLInputElement).value).toBe('');
      expect(screen.queryByText(token, { exact: false })).toBeNull();
      expect(document.activeElement).toBe(
        screen.getByRole('textbox', { name: 'Lectura del lector' }),
      );
    });

    it('searches as usual when Enter follows a name', async () => {
      const { rendered, http } = await openScreenWith([anOrder()]);
      const searchbox = screen.getByRole('searchbox', { name: 'Buscar pedido' });

      fireEvent.input(searchbox, { target: { value: 'maria' } });
      fireEvent.keyDown(searchbox, { key: 'Enter' });
      await rendered.fixture.whenStable();

      http.expectNone(SCAN_URL);
      expect(screen.getByText('K-4821')).not.toBeNull();
    });

    it('shows no orders until something is typed', async () => {
      await openScreenWith([anOrder()]);

      expect(screen.queryByText('K-4821')).toBeNull();
    });

    // US-18, criterion 3, moved here from the board on 2026-09-29.
    it('finds an order in preparation by name and marks it ready', async () => {
      const { rendered, http } = await openScreenWith([
        anOrder({ code: 'K-4821', customerName: 'María Quadro' }),
        anOrder({ code: 'K-0066', customerName: 'Pablo Díaz' }),
      ]);

      search('maria');
      await rendered.fixture.whenStable();

      expect(screen.queryByText('K-0066')).toBeNull();
      fireEvent.click(screen.getByRole('button', { name: 'Listo K-4821' }));
      expect(http.expectOne(markReadyUrl('K-4821')).request.method).toBe('POST');
    });

    // US-19, criterion 2: a phone with no battery left.
    it('finds a ready order by number, hands it over and offers to undo it', async () => {
      const { rendered, http } = await openScreenWith([anOrder({ status: 'Ready' })]);

      search('4821');
      await rendered.fixture.whenStable();
      fireEvent.click(screen.getByRole('button', { name: 'Entregado K-4821' }));
      http.expectOne(deliverUrl('K-4821')).flush(null, noContent);
      TestBed.tick();
      http.expectOne(KDS_QUEUE_URL).flush([]);
      await rendered.fixture.whenStable();

      fireEvent.click(screen.getByRole('button', { name: 'Deshacer' }));

      expect(http.expectOne(undoDeliveryUrl('K-4821')).request.method).toBe('POST');
    });

    // Found, but there is nothing to do with it here yet.
    it('shows a queued order without an action', async () => {
      const { rendered } = await openScreenWith([anOrder({ status: 'Queued' })]);

      search('4821');
      await rendered.fixture.whenStable();

      expect(screen.getByText('K-4821')).not.toBeNull();
      expect(screen.queryByRole('button', { name: /K-4821/ })).toBeNull();
    });
  });
});
