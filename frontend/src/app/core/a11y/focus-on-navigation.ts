import { DOCUMENT, DestroyRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router } from '@angular/router';
import { filter } from 'rxjs/operators';

/**
 * Moves focus to the new screen's heading after every navigation.
 *
 * A full page load resets focus on its own; client-side routing does not, so
 * without this someone using a keyboard or a screen reader is never told the
 * screen changed — the guard can bounce them from one screen to another and
 * their focus stays on whatever they last touched.
 *
 * It matters more than it looks here: the KDS and the till are meant to be
 * driven by a QR reader, which the browser sees as a keyboard.
 */
export function focusHeadingOnNavigation(): void {
  const router = inject(Router);
  const document = inject(DOCUMENT);
  const destroyRef = inject(DestroyRef);

  router.events
    .pipe(
      filter((event) => event instanceof NavigationEnd),
      takeUntilDestroyed(destroyRef),
    )
    .subscribe(() => {
      // After the router settles, the new screen's heading is in the document.
      const heading = document.querySelector('h1');

      if (heading === null) return;

      // Focusable only for this purpose: tabindex -1 takes focus by script
      // without adding a stop in the tab order.
      heading.setAttribute('tabindex', '-1');
      (heading as HTMLElement).focus({ preventScroll: true });
    });
}
