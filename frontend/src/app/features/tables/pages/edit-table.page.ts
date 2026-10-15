import { httpResource } from '@angular/common/http';
import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { problemTypeOf } from '../../../core/api/problem-type-of';
import { ProblemTypes } from '../../../core/api/problem-types';
import { AdminHeader } from '../../../shared/admin-header/admin-header';
import { EditTableStore } from '../edit-table.store';
import {
  CAPACITY_ERROR,
  LOCATION_ERROR,
  describedAs,
  locationFits,
  wholeBetweenOneAnd,
} from '../table-fields';
import { TABLES_URL, TABLE_CAPACITY_MAX, TABLE_LOCATION_MAX_LENGTH } from '../tables.service';
import type { VipTable } from '../tables.service';

@Component({
  selector: 'drinkit-edit-table-page',
  imports: [AdminHeader, ReactiveFormsModule, RouterLink],
  // On the component and not on the route: a route's injector is created once
  // and kept, so a store provided there would carry a failure from one table
  // to the next one opened.
  providers: [EditTableStore],
  styleUrl: './table-form.scss',
  templateUrl: './edit-table.page.html',
})
export class EditTablePage {
  protected readonly store = inject(EditTableStore);

  /** Both from the path, bound by the router. */
  readonly venueSlug = input.required<string>();

  readonly id = input.required<string>();

  protected readonly capacityMax = TABLE_CAPACITY_MAX;

  protected readonly locationMaxLength = TABLE_LOCATION_MAX_LENGTH;

  /**
   * The venue's tables, filtered here to the one being edited. There is no
   * endpoint that serves a single table, the same trade the staff edit makes:
   * the listing already fits in one response.
   */
  protected readonly tables = httpResource<VipTable[]>(() => TABLES_URL);

  protected readonly table = computed<VipTable | undefined>(() =>
    (this.tables.hasValue() ? this.tables.value() : []).find((table) => table.id === this.id()),
  );

  /** Loaded, and this venue has no table with that id: from here, it does not exist. */
  protected readonly isUnknown = computed(() => this.tables.hasValue() && !this.table());

  protected readonly loadFailure = computed<'none' | 'forbidden' | 'unreachable'>(() => {
    const error = this.tables.error();

    if (error === undefined) return 'none';

    return problemTypeOf(error) === ProblemTypes.forbidden ? 'forbidden' : 'unreachable';
  });

  protected readonly form = new FormGroup({
    capacity: new FormControl('', {
      nonNullable: true,
      validators: [wholeBetweenOneAnd(TABLE_CAPACITY_MAX)],
    }),
    location: new FormControl('', { nonNullable: true, validators: [locationFits] }),
  });

  /** Errors stay hidden until the first save: flagging a field mid-typing is noise. */
  private readonly attempted = signal(false);

  /** Reactive forms report through RxJS and this app is zoneless: bridged into signals. */
  private readonly typed = toSignal(this.form.valueChanges, {
    initialValue: this.form.getRawValue(),
  });

  protected readonly capacityError = computed(() => {
    this.typed();

    if (!this.attempted() || this.store.isSending()) return null;

    return this.form.controls.capacity.invalid ? CAPACITY_ERROR : null;
  });

  protected readonly locationError = computed(() => {
    this.typed();

    if (!this.attempted() || this.store.isSending()) return null;

    return this.form.controls.location.invalid ? LOCATION_ERROR : null;
  });

  constructor() {
    // The form starts from what the table has, once it arrives. Only then:
    // refilling on every change would undo what is being typed.
    let filled = false;

    effect(() => {
      const table = this.table();

      if (!table || filled) return;

      filled = true;
      this.form.setValue({ capacity: `${table.capacity}`, location: table.location ?? '' });
    });

    // Reactive forms are not signal-aware, so enabling and disabling is driven
    // from here rather than bound in the template.
    effect(() => {
      if (this.store.isSending()) this.form.disable();
      else this.form.enable();
    });
  }

  protected submit(): void {
    this.attempted.set(true);

    if (this.form.invalid || this.store.isSending()) return;

    const { capacity, location } = this.form.getRawValue();

    this.store.submit(this.venueSlug(), this.id(), describedAs(capacity, location));
  }
}
