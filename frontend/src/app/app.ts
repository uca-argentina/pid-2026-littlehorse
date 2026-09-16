import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { focusHeadingOnNavigation } from './core/a11y/focus-on-navigation';
import { ThemeStorage } from './core/theme/theme-storage';

@Component({
  imports: [RouterOutlet],
  selector: 'drinkit-root',
  styleUrl: './app.scss',
  templateUrl: './app.html',
})
export class App {
  // Instantiated here so the stored preference applies from the very first
  // screen, not only once someone reaches an administration page and its
  // header injects this the same singleton.
  private readonly theme = inject(ThemeStorage);

  constructor() {
    focusHeadingOnNavigation();
  }
}
