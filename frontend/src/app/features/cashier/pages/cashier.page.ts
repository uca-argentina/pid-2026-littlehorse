import { DatePipe, formatDate } from '@angular/common';
import { HttpErrorResponse, httpResource } from '@angular/common/http';
import type { ElementRef } from '@angular/core';
import {
  Component,
  DestroyRef,
  Injector,
  LOCALE_ID,
  afterNextRender,
  computed,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import type { Observable } from 'rxjs';
import { ProblemTypes } from '../../../core/api/problem-types';
import { problemTypeOf } from '../../../core/api/problem-type-of';
import { formatPrice } from '../../../shared/money/price';
import { QrCamera } from '../../../shared/qr-camera/qr-camera';
import { StaffHeader } from '../../../shared/staff-header/staff-header';
import { CASHIER_ORDERS_URL, CashierService, MY_COLLECTIONS_URL } from '../cashier.service';
import type { CashierOrder } from '../cashier.service';
import { OrderToCollect } from '../components/order-to-collect';
import { ReaderBurst } from '../reader-burst';
import { TillChannel } from '../till-channel';

/**
 * What the last lookup or collection came to. Data and not a sentence: the
 * template says it, in Spanish.
 */
type Outcome =
  | { readonly kind: 'collected'; readonly code: string }
  | { readonly kind: 'notFound'; readonly code: string }
  | { readonly kind: 'unknownQr' }
  | { readonly kind: 'alreadyPaid'; readonly code: string; readonly paidAt: string | null }
  | { readonly kind: 'searchFailed' }
  | { readonly kind: 'collectFailed' };

/** What the customer's QR carries: the order's tracking token. Codes have a dash. */
const TOKEN = /^[0-9a-f]{32}$/i;

/**
 * The till (US-26), from design/wireframes/CajeroBuscar and CajeroConfirmar.
 * The cashier never puts an order together (§12): the customer shows a QR or
 * a code, the till finds the order behind it, and the cashier confirms the
 * money is in. From there the order is the bar's.
 *
 * One field takes all three ways in: a USB reader types the QR and presses
 * Enter, somebody types the code by hand, and the camera hands over what it
 * read. Beside it, what is still waiting for cash and what this cashier has
 * collected in the shift.
 */
@Component({
  selector: 'drinkit-cashier-page',
  imports: [DatePipe, OrderToCollect, QrCamera, StaffHeader],
  styleUrl: './cashier.page.scss',
  templateUrl: './cashier.page.html',
  host: { '(document:keydown)': 'keyPressed($event)' },
})
export class CashierPage {
  private readonly cashier = inject(CashierService);

  private readonly destroyRef = inject(DestroyRef);

  private readonly injector = inject(Injector);

  private readonly locale = inject(LOCALE_ID);

  /** Hears when what waits for cash changed, so "Por cobrar" reloads on its own. */
  protected readonly channel = inject(TillChannel);

  private readonly entry = viewChild<ElementRef<HTMLInputElement>>('entry');

  readonly venueSlug = input.required<string>();

  /** Everything still waiting for cash, oldest first. */
  protected readonly pending = httpResource<CashierOrder[]>(() => CASHIER_ORDERS_URL);

  /** "Cobros de tu turno": this cashier's, the latest first. */
  protected readonly collected = httpResource<CashierOrder[]>(() => MY_COLLECTIONS_URL);

  protected readonly collectedTotal = computed(() =>
    (this.collected.hasValue() ? this.collected.value() : []).reduce(
      (sum, order) => sum + order.total,
      0,
    ),
  );

  /** What "Por cobrar" is narrowed to: a code or a name, typed in its own field. */
  protected readonly filter = signal('');

  protected readonly pendingShown = computed(() => {
    const orders = this.pending.hasValue() ? this.pending.value() : [];
    const wanted = folded(this.filter().trim());

    if (wanted === '') return orders;

    return orders.filter(
      (order) => folded(order.code).includes(wanted) || folded(order.customerName).includes(wanted),
    );
  });

  private readonly burst = new ReaderBurst();

  protected readonly read = signal('');

  protected readonly cameraOpen = signal(false);

  /** The order on screen, ready to collect. Only ever one still waiting for money. */
  protected readonly selected = signal<CashierOrder | null>(null);

  protected readonly outcome = signal<Outcome | null>(null);

  protected readonly outcomeCode = computed(() => {
    const outcome = this.outcome();

    return outcome !== null && 'code' in outcome ? outcome.code : '';
  });

  /** " desde las 01:15", or nothing when the API refused a second collection without saying when. */
  protected readonly paidSince = computed(() => {
    const outcome = this.outcome();

    if (outcome?.kind !== 'alreadyPaid' || outcome.paidAt === null) return '';

    return ` desde las ${formatDate(outcome.paidAt, 'HH:mm', this.locale)}`;
  });

  protected readonly searching = signal(false);

  protected readonly collecting = signal(false);

  protected readonly price = formatPrice;

  constructor() {
    this.focusTheEntry();

    // Also after the link comes back from a gap: whatever was confirmed or
    // collected meanwhile only shows up by asking again.
    this.channel.connect(() => this.pending.reload());
    this.destroyRef.onDestroy(() => this.channel.disconnect());
  }

  /**
   * A USB reader types wherever the cursor is. In the till's field the form
   * takes it; in another field it is somebody typing there; anywhere else — a
   * list button, the page — a burst ending in Enter is still a read, and its
   * Enter must not press the button that had the focus.
   */
  protected keyPressed(event: KeyboardEvent): void {
    const target = event.target;

    if (target instanceof HTMLInputElement || target instanceof HTMLTextAreaElement) return;

    const read = this.burst.key(event.key, event.timeStamp);

    if (read === null) return;

    event.preventDefault();
    this.lookUp(read);
  }

  protected search(event: Event): void {
    event.preventDefault();
    this.lookUp(this.read());
  }

  /** The camera reads the same QR many times a second: the first one is enough. */
  protected closeCamera(): void {
    this.cameraOpen.set(false);
    this.focusTheEntry();
  }

  protected cameraRead(read: string): void {
    if (this.searching()) return;

    this.cameraOpen.set(false);
    this.lookUp(read);
  }

  /** From the list: the same as reading its code, without the round trip. */
  protected open(order: CashierOrder): void {
    this.outcome.set(null);
    this.cameraOpen.set(false);
    this.show(order);
  }

  protected cancel(): void {
    this.selected.set(null);
    this.focusTheEntry();
  }

  protected collect(): void {
    const order = this.selected();

    if (order === null || this.collecting()) return;

    this.collecting.set(true);
    this.outcome.set(null);

    this.cashier
      .collect(order.code)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.collecting.set(false);
          this.selected.set(null);
          this.read.set('');
          this.outcome.set({ kind: 'collected', code: order.code });
          this.reloadTheLists();
          this.focusTheEntry();
        },
        error: (error: unknown) => {
          this.collecting.set(false);

          const problem = problemTypeOf(error);

          // Refused on its merits: the order is no longer something to collect,
          // so it comes off the screen. A dropped connection leaves it there to
          // try again — nothing was charged.
          if (problem === ProblemTypes.cashierAlreadyPaid || isNotFound(error)) {
            this.selected.set(null);
            this.reloadTheLists();
          }

          if (problem === ProblemTypes.cashierAlreadyPaid) {
            this.outcome.set({ kind: 'alreadyPaid', code: order.code, paidAt: null });
          } else if (isNotFound(error)) {
            this.outcome.set({ kind: 'notFound', code: order.code });
          } else {
            this.outcome.set({ kind: 'collectFailed' });
          }
        },
      });
  }

  /**
   * A QR's token goes to the scan, in a body; anything else is a code, and
   * codes are printed in capitals nobody should have to type.
   */
  private lookUp(raw: string): void {
    const read = raw.trim();

    if (read === '' || this.searching()) return;

    const isQr = TOKEN.test(read);
    const code = read.toUpperCase();
    const found: Observable<CashierOrder> = isQr
      ? this.cashier.scan(read)
      : this.cashier.find(code);

    this.selected.set(null);
    this.outcome.set(null);
    this.searching.set(true);

    found.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (order) => {
        this.searching.set(false);
        this.read.set('');
        this.show(order);
      },
      error: (error: unknown) => {
        this.searching.set(false);

        if (!isNotFound(error)) this.outcome.set({ kind: 'searchFailed' });
        else if (isQr) this.outcome.set({ kind: 'unknownQr' });
        else this.outcome.set({ kind: 'notFound', code });

        this.focusTheEntry();
      },
    });
  }

  private show(order: CashierOrder): void {
    if (order.status === 'AwaitingPayment') {
      this.selected.set(order);

      return;
    }

    this.outcome.set({ kind: 'alreadyPaid', code: order.code, paidAt: order.paidAt });
    this.focusTheEntry();
  }

  private reloadTheLists(): void {
    this.pending.reload();
    this.collected.reload();
  }

  /** Back where a USB reader types, once the field is on screen again. */
  private focusTheEntry(): void {
    afterNextRender(() => this.entry()?.nativeElement.focus(), { injector: this.injector });
  }
}

/** Lower case and without accents, so "maria" finds María. */
function folded(text: string): string {
  return text
    .normalize('NFD')
    .replace(/\p{Diacritic}/gu, '')
    .toLowerCase();
}

function isNotFound(error: unknown): boolean {
  return error instanceof HttpErrorResponse && error.status === 404;
}
