import { httpResource } from '@angular/common/http';
import { Component, computed, effect, inject, input } from '@angular/core';
import { Router } from '@angular/router';
import { problemTypeOf } from '../../../core/api/problem-type-of';
import { anonymously } from '../../../core/auth/anonymous-request';
import { ProblemTypes } from '../../../core/api/problem-types';
import { VipTables, vipTableUrl } from '../../../core/vip-table/vip-tables';
import type { VipTableAccess } from '../../../core/vip-table/vip-tables';
import { GlassMark } from '../../../shared/glass-mark/glass-mark';

/**
 * Where a VIP table's QR lands (US-41, §8.2). It asks the venue whether the
 * code opens a table tonight, and if so remembers it on the phone and goes on
 * to the menu, which orders for that table from then on. It never says which
 * of the ways of not working this code hit (criterion 4).
 */
@Component({
  selector: 'drinkit-table-entry-page',
  imports: [GlassMark],
  styleUrl: './table-entry.page.scss',
  templateUrl: './table-entry.page.html',
})
export class TableEntryPage {
  private readonly tables = inject(VipTables);

  private readonly router = inject(Router);

  /** Both from the QR's address, bound by the router. */
  readonly venueSlug = input.required<string>();

  readonly code = input.required<string>();

  // Anonymous, like the menu: a staff session left on this phone is not who
  // the waiter handed the QR to.
  protected readonly table = httpResource<VipTableAccess>(() => ({
    url: vipTableUrl(this.venueSlug(), this.code()),
    context: anonymously(),
  }));

  /** A code that opens nothing tonight, as opposed to a signal that dropped. */
  protected readonly isGone = computed(() => {
    const error = this.table.error();

    return error !== undefined && problemTypeOf(error) === ProblemTypes.vipAccountNotFound;
  });

  protected readonly isUnreachable = computed(
    () => this.table.error() !== undefined && !this.isGone(),
  );

  constructor() {
    effect(() => {
      if (!this.table.hasValue()) return;

      this.tables.remember(this.venueSlug(), this.code());
      // Replacing the QR's address: going back from the menu should not open
      // the table all over again.
      void this.router.navigate(['/', this.venueSlug(), 'menu'], { replaceUrl: true });
    });
  }

  protected orderForTheBar(): void {
    this.tables.forget(this.venueSlug());
    void this.router.navigate(['/', this.venueSlug(), 'menu'], { replaceUrl: true });
  }

  protected retry(): void {
    this.table.reload();
  }
}
