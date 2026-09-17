import { Component, input } from '@angular/core';

/**
 * The venue's glass mark, drawn wherever a screen needs it — the menu's
 * header, the order's header, one line per drink. currentColor and no fixed
 * viewBox size, so it always reads with whoever's font colour placed it.
 */
@Component({
  selector: 'drinkit-glass-mark',
  templateUrl: './glass-mark.html',
  // A custom element defaults to display:inline, and an inline svg inside it
  // sits on the text baseline — off-centre in a flex parent that expects to
  // centre a plain block. flex makes this element behave like the svg would.
  styles: ':host { display: flex; }',
})
export class GlassMark {
  readonly size = input(20);

  readonly strokeWidth = input(2);
}
