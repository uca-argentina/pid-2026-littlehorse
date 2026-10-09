import { Component, computed, input, output, signal } from '@angular/core';

/** One account the picker offers. */
export interface CrewAccount {
  readonly id: string;
  readonly username: string;
}

/** Past this many accounts, the open list gets a search field. */
const SEARCH_FROM = 7;

let nextId = 0;

/**
 * The accounts of one role, as a dropdown of checkboxes. Dumb on purpose: it
 * says what was ticked and the form decides. Being open or closed, and what
 * was typed in its search, are its own business.
 */
@Component({
  selector: 'drinkit-crew-picker',
  styleUrl: './crew-picker.scss',
  templateUrl: './crew-picker.html',
  host: { '(keydown.escape)': 'close()' },
})
export class CrewPicker {
  readonly title = input.required<string>();

  readonly accounts = input.required<readonly CrewAccount[]>();

  readonly chosen = input.required<ReadonlySet<string>>();

  /** What the open list says when the role has nobody. */
  readonly whenNone = input.required<string>();

  readonly disabled = input(false);

  readonly error = input<string | null>(null);

  readonly toggled = output<string>();

  protected readonly isOpen = signal(false);

  protected readonly search = signal('');

  private readonly id = nextId++;

  protected readonly labelId = `crew-${this.id}-label`;

  protected readonly summaryId = `crew-${this.id}-summary`;

  protected readonly panelId = `crew-${this.id}-panel`;

  protected readonly searchId = `crew-${this.id}-search`;

  protected readonly errorId = `crew-${this.id}-error`;

  protected readonly showsSearch = computed(() => this.accounts().length >= SEARCH_FROM);

  /** Closed, the button is all there is: it names who is in. */
  protected readonly summary = computed(() => {
    const names = this.accounts()
      .filter((account) => this.chosen().has(account.id))
      .map((account) => account.username);

    if (names.length === 0) return 'Nadie elegido';
    if (names.length <= 2) return names.join(', ');

    return `${names[0]}, ${names[1]} y ${names.length - 2} más`;
  });

  protected readonly visible = computed(() => {
    const term = this.search().trim().toLowerCase();

    if (term === '') return this.accounts();

    return this.accounts().filter((account) => account.username.toLowerCase().includes(term));
  });

  protected flip(): void {
    this.isOpen.update((open) => !open);
  }

  protected close(): void {
    this.isOpen.set(false);
  }

  protected searchFor(event: Event): void {
    this.search.set((event.target as HTMLInputElement).value);
  }
}
