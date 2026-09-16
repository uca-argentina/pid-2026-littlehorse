import { Component, inject } from '@angular/core';
import { ThemeStorage } from '../../core/theme/theme-storage';

/**
 * The sun/moon switch, shared by every header that has one. Self-contained:
 * no inputs, it reads and flips ThemeStorage directly, so a screen only has
 * to drop the tag in.
 */
@Component({
  selector: 'drinkit-theme-toggle',
  styleUrl: './theme-toggle.scss',
  templateUrl: './theme-toggle.html',
})
export class ThemeToggle {
  protected readonly theme = inject(ThemeStorage);
}
