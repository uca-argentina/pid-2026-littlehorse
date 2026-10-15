import { DOCUMENT } from '@angular/common';
import { Component, DestroyRef, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { QRCodeComponent } from 'angularx-qrcode';
import { formatPrice } from '../../../../shared/money/price';
import type { NightPhase } from '../../night-calendar';
import { NightsService } from '../../nights.service';
import type { NightTable } from '../../nights.service';

/**
 * US-40: the code of each table the night sells, as the QR the waiter hands
 * over (§8.2) — never pasted on the table. A code that leaked is replaced,
 * which cuts off whoever had it and keeps the balance.
 */
@Component({
  selector: 'drinkit-night-table-codes',
  imports: [QRCodeComponent],
  styleUrl: './night-table-codes.scss',
  templateUrl: './night-table-codes.html',
})
export class NightTableCodes {
  private readonly nights = inject(NightsService);

  private readonly destroyRef = inject(DestroyRef);

  private readonly origin = inject(DOCUMENT).location.origin;

  readonly venueSlug = input.required<string>();

  readonly nightId = input.required<string>();

  readonly phase = input.required<NightPhase>();

  readonly tables = input.required<readonly NightTable[]>();

  /** Codes given after the page loaded, by table. They win over what was loaded. */
  private readonly renewed = signal<ReadonlyMap<string, string>>(new Map());

  /** The table whose new code is waiting for a yes. */
  protected readonly confirming = signal<string | null>(null);

  protected readonly regenerating = signal(false);

  protected readonly failedFor = signal<string | null>(null);

  /** A finished night's codes no longer open anything, so none is shown. */
  protected readonly cards = computed(() =>
    this.phase() === 'over'
      ? []
      : this.tables().map((table) => {
          const code = this.renewed().get(table.tableId) ?? table.accessCode;

          return {
            ...table,
            link: `${this.origin}/${this.venueSlug()}/table/${code}`,
            left: `Quedan $ ${formatPrice(table.budget - table.spent)} de $ ${formatPrice(table.budget)}`,
          };
        }),
  );

  protected readonly canShare = typeof navigator !== 'undefined' && 'share' in navigator;

  protected readonly qrInk = '#141210';

  protected readonly qrPaper = '#ffffff';

  protected ask(tableId: string): void {
    this.failedFor.set(null);
    this.confirming.set(tableId);
  }

  protected cancel(): void {
    this.confirming.set(null);
  }

  protected regenerate(tableId: string): void {
    if (this.regenerating()) return;

    this.regenerating.set(true);

    this.nights
      .regenerateTableCode(this.nightId(), tableId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (code) => {
          this.regenerating.set(false);
          this.confirming.set(null);
          this.renewed.update((renewed) => new Map(renewed).set(code.tableId, code.accessCode));
        },
        error: () => {
          this.regenerating.set(false);
          this.confirming.set(null);
          this.failedFor.set(tableId);
        },
      });
  }

  protected copy(link: string): void {
    void navigator.clipboard?.writeText(link);
  }

  /** WhatsApp and the rest, from the phone's own share sheet (§8.2). */
  protected share(number: number, link: string): void {
    void navigator.share({ title: `Mesa ${number}`, text: `Tu mesa VIP: ${link}` }).catch(() => {
      // Closing the share sheet is not an error worth telling anybody about.
    });
  }
}
