import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { focusHeadingOnNavigation } from './core/a11y/focus-on-navigation';

@Component({
  imports: [RouterOutlet],
  selector: 'drinkit-root',
  styleUrl: './app.scss',
  templateUrl: './app.html',
})
export class App {
  constructor() {
    focusHeadingOnNavigation();
  }
}
