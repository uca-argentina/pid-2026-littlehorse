import { HttpErrorResponse } from '@angular/common/http';
import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import type { Observable } from 'rxjs';
import { problemTypeOf } from '../../core/api/problem-type-of';
import { ProblemTypes } from '../../core/api/problem-types';
import { NightsService } from './nights.service';
import type { NewNight, Night } from './nights.service';

/** One value rather than a set of booleans, so two of them cannot be true at once. */
export type SaveNightStatus =
  | 'idle'
  | 'sending'
  | 'overlaps'
  | 'crewChanged'
  | 'over'
  | 'startLocked'
  | 'barHoldsStock'
  | 'unreachable';

/**
 * Saving a night, new or edited: the same answers either way. Provided by the
 * page component, so a stale failure dies with it, as in NewStaffUserStore.
 */
@Injectable()
export class SaveNightStore {
  private readonly nights = inject(NightsService);

  private readonly router = inject(Router);

  private readonly destroyRef = inject(DestroyRef);

  private readonly state = signal<SaveNightStatus>('idle');

  readonly status = this.state.asReadonly();

  readonly isSending = computed(() => this.state() === 'sending');

  /**
   * Opens the new night once it exists: setting it up goes on with loading
   * what each KDS will have that night (US-37), which lives in its screen.
   */
  create(venueSlug: string, night: NewNight): void {
    this.send(
      () => this.nights.create(night),
      (saved) => [venueSlug, 'staff', 'nights', saved.id],
    );
  }

  /** Straight back to the listing, where US-35 says the night shows up. */
  update(venueSlug: string, id: string, night: NewNight): void {
    this.send(
      () => this.nights.update(id, night),
      () => [venueSlug, 'staff', 'nights'],
    );
  }

  private send(request: () => Observable<Night>, next: (saved: Night) => string[]): void {
    if (this.isSending()) return;

    this.state.set('sending');

    request()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (saved) => this.go(next(saved)),
        error: (error: unknown) => this.state.set(reasonFor(error)),
      });
  }

  private go(path: string[]): void {
    this.state.set('idle');

    this.router.navigate(path).then(
      (navigated) => {
        if (!navigated) this.state.set('unreachable');
      },
      () => this.state.set('unreachable'),
    );
  }
}

const REASONS = new Map<string, SaveNightStatus>([
  [ProblemTypes.nightOverlaps, 'overlaps'],
  [ProblemTypes.nightCrewMemberNotFound, 'crewChanged'],
  [ProblemTypes.nightOver, 'over'],
  [ProblemTypes.nightStartLocked, 'startLocked'],
  [ProblemTypes.nightBarHoldsStock, 'barHoldsStock'],
]);

/**
 * Only the failures an administrator can act on get their own answer. A dropped
 * connection or a 500 mean the same from here: nothing was saved.
 */
function reasonFor(error: unknown): SaveNightStatus {
  if (!(error instanceof HttpErrorResponse)) return 'unreachable';

  return REASONS.get(problemTypeOf(error) ?? '') ?? 'unreachable';
}

const REFUSALS: Partial<Record<SaveNightStatus, string>> = {
  overlaps: 'Ese horario se superpone con otra noche del boliche. Corré el inicio o el fin.',
  crewChanged:
    'Una de las cuentas elegidas ya no está en el boliche. Recargá la pantalla y elegí de nuevo.',
  over: 'La noche terminó mientras la editabas, y una noche terminada ya no se cambia.',
  startLocked: 'La noche empezó mientras la editabas: el inicio ya no se puede mover.',
  barHoldsStock:
    'Una de las KDS que sacaste todavía tiene stock en esta noche. Devolvé su stock al depósito desde el stock de la noche y después sacala.',
  unreachable: 'No pudimos guardarla. Fijate la señal y probá de nuevo.',
};

/** What the form says next to its button for each refusal; null while there is none. */
export function refusalFor(status: SaveNightStatus): string | null {
  return REFUSALS[status] ?? null;
}
