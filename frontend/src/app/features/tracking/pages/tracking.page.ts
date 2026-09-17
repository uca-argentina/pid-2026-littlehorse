import { Component, computed, effect, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TrackingStore } from '../tracking.store';

/** One step of the journey, as the screen draws it. */
interface Step {
  readonly name: string;
  readonly reached: boolean;
}

/**
 * The four steps of the journey, and which server statuses count as having
 * reached each one.
 *
 * Paid and Queued are one step: for somebody holding a phone they are the same
 * thing — already paid, still waiting. The two statuses that end the journey
 * are not here: Delivered is the last step, and Canceled is not a step at all.
 */
const JOURNEY = [
  {
    name: 'Esperando en la barra',
    after: ['Paid', 'Queued', 'InPreparation', 'Ready', 'Delivered'],
  },
  { name: 'En preparación', after: ['InPreparation', 'Ready', 'Delivered'] },
  { name: 'Listo', after: ['Ready', 'Delivered'] },
  { name: 'Entregado', after: ['Delivered'] },
];

/** What to say about each status, in the words somebody in a bar would use. */
const WHAT_IS_HAPPENING: Record<string, string> = {
  Paid: 'Ya está pago. Te avisamos cuando lo estén preparando.',
  Queued: 'Ya está pago y esperando en la barra.',
  InPreparation: 'Lo están preparando.',
  Ready: '¡Está listo! Acercate a la barra y decí tu código.',
  Delivered: 'Entregado. ¡Que lo disfrutes!',
  Canceled: 'Este pedido fue cancelado.',
};

/**
 * Where somebody's order is.
 *
 * The only screen of the app that changes without anybody touching it: it asks
 * the server every three seconds, which is what US-12 asks for and what lets
 * somebody stay at their table instead of standing at the bar.
 *
 * It is also the screen somebody lands on the moment they pay, so it opens with
 * the code big enough to read across a room — that is the first thing they need
 * — and the journey underneath.
 */
@Component({
  selector: 'drinkit-tracking-page',
  imports: [RouterLink],
  providers: [TrackingStore],
  styleUrl: './tracking.page.scss',
  templateUrl: './tracking.page.html',
})
export class TrackingPage {
  protected readonly store = inject(TrackingStore);

  readonly venueSlug = input.required<string>();

  /** From the path: the order's name, "K-4821". */
  readonly code = input.required<string>();

  /** From the path: the secret that says this order is theirs. */
  readonly token = input.required<string>();

  protected readonly menuLink = computed(() => ['/', this.venueSlug(), 'menu']);

  private readonly status = computed(() => this.store.order()?.status ?? '');

  protected readonly wasCancelled = computed(() => this.status() === 'Canceled');

  protected readonly whatIsHappening = computed(() => WHAT_IS_HAPPENING[this.status()] ?? '');

  protected readonly steps = computed<Step[]>(() =>
    JOURNEY.map((step) => ({ name: step.name, reached: step.after.includes(this.status()) })),
  );

  constructor() {
    effect(() => this.store.follow(this.venueSlug(), this.code(), this.token()));
  }
}
