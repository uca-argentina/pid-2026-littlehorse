import { httpResource } from '@angular/common/http';
import {
  Component,
  DestroyRef,
  InjectionToken,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import type { ElementRef } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { SessionStorage } from '../../../core/auth/session-storage';
import { KDS_RETRY_MS, KdsBoardChannel } from '../../../core/kds/kds-board-channel';
import { VenueBrand } from '../../../shared/venue-brand/venue-brand';
import { KdsCamera } from '../components/kds-camera';
import { matchesSearch } from '../kds-board-view';
import { KdsOrderActions } from '../kds-order-actions';
import { KDS_QUEUE_URL } from '../kds-queue';
import type { KdsOrderStatus, KdsQueueOrder } from '../kds-queue';
import { KdsScanStore, isTrackingToken } from '../kds-scan.store';
import type { ScanResult } from '../kds-scan.store';

/**
 * How long the camera stays open without reading anything. Left open with
 * nobody in front of it, it would keep its light on and the tablet's battery
 * going all night.
 */
export const CAMERA_IDLE_MS = new InjectionToken<number>('CAMERA_IDLE_MS', {
  providedIn: 'root',
  factory: () => 30_000,
});

/** How long the scan block flashes when a code goes out. */
export const SCAN_FLASH_MS = new InjectionToken<number>('SCAN_FLASH_MS', {
  providedIn: 'root',
  factory: () => 600,
});

/** How often "hace N seg" is read again. */
const CLOCK_TICK_MS = 5_000;

/** What each scan result says, in the words of the bar. */
interface ScanLine {
  readonly tag: string;
  readonly tone: 'done' | 'warn';
  readonly who: string | null;
  readonly note: string | null;
}

const STATUS_NAMES: Record<KdsOrderStatus, string> = {
  Queued: 'En la cola',
  InPreparation: 'En preparación',
  Ready: 'Listo',
};

/**
 * The bar's scan screen (US-20, closing US-19): wireframe KdsEscanear.
 *
 * One scan, no mode — a ready order is handed over, anything else is said and
 * left as it was. The camera and a reader plugged into the tablet end in the
 * same place. Below them, the manual search that used to be on the board
 * (moved on 2026-09-29): for a ticket that will not read or a phone with a
 * dead battery.
 */
@Component({
  selector: 'drinkit-kds-scan-page',
  imports: [KdsCamera, RouterLink, VenueBrand],
  providers: [KdsScanStore, KdsOrderActions],
  styleUrl: './kds-scan.page.scss',
  templateUrl: './kds-scan.page.html',
})
export class KdsScanPage {
  readonly venueSlug = input.required<string>();

  protected readonly scans = inject(KdsScanStore);

  private readonly channel = inject(KdsBoardChannel);

  private readonly actions = inject(KdsOrderActions);

  private readonly destroyRef = inject(DestroyRef);

  private readonly sessions = inject(SessionStorage);

  private readonly router = inject(Router);

  protected readonly station = computed(
    () => `${this.sessions.session()?.username ?? ''} · escaneo`,
  );

  protected readonly boardLink = computed(() => ['/', this.venueSlug(), 'staff', 'kds']);

  /** Only for the manual search: the scan itself asks the API directly. */
  protected readonly queue = httpResource<KdsQueueOrder[]>(() => KDS_QUEUE_URL);

  protected readonly search = signal('');

  private readonly reader = viewChild.required<ElementRef<HTMLInputElement>>('reader');

  private readonly cameraIdleMs = inject(CAMERA_IDLE_MS);

  private readonly flashMs = inject(SCAN_FLASH_MS);

  /**
   * Closed until asked for, like KdsEscanear: the block waits for a reader, and
   * the camera opens for one customer and closes once it has read them.
   */
  protected readonly cameraOpen = signal(false);

  /** A code just went out: the block flashes, to be seen from the corner of an eye. */
  protected readonly flashing = signal(false);

  private cameraIdle: ReturnType<typeof setTimeout> | null = null;

  private flashTimer: ReturnType<typeof setTimeout> | null = null;

  protected readonly found = computed(() => {
    const query = this.search();

    if (query.trim() === '' || !this.queue.hasValue()) return [];

    return this.queue.value().filter((order) => matchesSearch(order, query));
  });

  protected readonly latest = computed(() => this.scans.recent()[0] ?? null);

  private readonly now = signal(Date.now());

  /** What the last action on an order could not do, said to the bar. */
  protected readonly actionFailure = this.actions.failure;

  /** The order just handed over, while its "Deshacer" is on offer. */
  protected readonly justDelivered = this.actions.justDelivered;

  constructor() {
    this.channel.connect(() => this.queue.reload());

    // Whatever an action did, the search shows the order as it is now.
    this.actions.whenSettled(() => this.queue.reload());

    // A scan that handed an order over can be undone for a few seconds, like
    // a tap on "Entregado": a mistaken scan is the same mistake.
    effect(() => {
      const scan = this.latest();

      if (scan?.kind === 'found' && scan.outcome === 'Delivered')
        untracked(() => this.actions.offerUndo(scan.code));
    });

    // A reader plugged into the tablet types into whatever has the focus, so
    // arriving on this screen puts it on the reader's field. Moved here on
    // purpose, not with autofocus: it is this screen's whole job.
    afterNextRender(() => this.reader().nativeElement.focus());

    // Same as the board (US-32): a session that ends while the tablet sits
    // on this screen sends it to sign in.
    effect(() => {
      if (this.sessions.session() !== null) return;

      void this.router.navigate(['/', this.venueSlug(), 'staff', 'login'], {
        queryParams: this.sessions.expired() ? { expired: true } : {},
      });
    });

    effect((onCleanup) => {
      if (this.queue.status() !== 'error') return;

      const retry = setTimeout(() => this.queue.reload(), KDS_RETRY_MS);
      onCleanup(() => clearTimeout(retry));
    });

    const ticking = setInterval(() => this.now.set(Date.now()), CLOCK_TICK_MS);

    this.destroyRef.onDestroy(() => {
      clearInterval(ticking);
      if (this.cameraIdle !== null) clearTimeout(this.cameraIdle);
      if (this.flashTimer !== null) clearTimeout(this.flashTimer);
      this.channel.disconnect();
    });
  }

  /** What a reader typed, on its Enter. The field is emptied for the next one. */
  protected readerRead(field: HTMLInputElement): void {
    this.send(field.value);
    field.value = '';
  }

  /**
   * Enter in the searchbox. A reader types wherever the focus is — after a
   * search, here — and what it typed is a scan: it goes out as one, leaves
   * the screen, and the focus goes back to the reader's field.
   */
  protected searchEntered(field: HTMLInputElement): void {
    if (!isTrackingToken(field.value)) return;

    this.send(field.value);
    field.value = '';
    this.search.set('');
    this.reader().nativeElement.focus();
  }

  protected openCamera(): void {
    this.cameraOpen.set(true);
    this.cameraIdle = setTimeout(() => this.closeCamera(), this.cameraIdleMs);
  }

  /** Closed, and the focus back where a reader types: the next customer may use one. */
  protected closeCamera(): void {
    if (this.cameraIdle !== null) clearTimeout(this.cameraIdle);
    this.cameraIdle = null;
    this.cameraOpen.set(false);
    this.reader().nativeElement.focus();
  }

  /** One customer per opening: once it has read a code, the camera has done its job. */
  protected cameraRead(code: string): void {
    this.send(code);
    this.closeCamera();
  }

  private send(read: string): void {
    if (!this.scans.submit(read)) return;

    if (this.flashTimer !== null) clearTimeout(this.flashTimer);
    this.flashing.set(true);
    this.flashTimer = setTimeout(() => this.flashing.set(false), this.flashMs);
  }

  protected lineFor(scan: ScanResult): ScanLine {
    if (scan.kind === 'unknown')
      return {
        tag: 'No reconocemos este código',
        tone: 'warn',
        who: null,
        note: 'No es un pedido de este local.',
      };

    // The answer was lost, not necessarily the scan: the order may be
    // delivered already, and the next scan would then say so.
    if (scan.kind === 'failed')
      return {
        tag: 'No sabemos si se registró',
        tone: 'warn',
        who: null,
        note: 'Se cortó la conexión. Escaneá de nuevo: si dice “Ya se entregó”, fue este escaneo.',
      };

    const who = `${scan.code} · ${scan.customerName}`;

    switch (scan.outcome) {
      case 'Delivered':
        return { tag: 'Entregado', tone: 'done', who, note: null };
      case 'NotReadyYet':
        return { tag: 'Todavía no está listo', tone: 'warn', who, note: 'No lo entregues.' };
      case 'AlreadyDelivered':
        return {
          tag: 'Ya se entregó',
          tone: 'warn',
          who,
          note: 'Alguien ya lo retiró. No lo entregues de nuevo.',
        };
    }
  }

  protected ago(scan: ScanResult): string {
    const seconds = Math.max(0, Math.round((this.now() - scan.at) / 1000));

    return seconds < 60 ? `hace ${seconds} seg` : `hace ${Math.floor(seconds / 60)} min`;
  }

  protected statusName(order: KdsQueueOrder): string {
    return STATUS_NAMES[order.status];
  }

  protected isBusy(order: KdsQueueOrder): boolean {
    return this.actions.isBusy(order.code);
  }

  protected markReady(order: KdsQueueOrder): void {
    this.actions.markReady(order.code);
  }

  protected deliver(order: KdsQueueOrder): void {
    this.actions.deliver(order.code);
  }

  protected undoDelivery(code: string): void {
    this.actions.undoDelivery(code);
  }
}
