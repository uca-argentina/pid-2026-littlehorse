import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, RouterOutlet, provideRouter } from '@angular/router';
import { focusHeadingOnNavigation } from './focus-on-navigation';

@Component({ template: '<h1>Primera</h1>' })
class First {}

@Component({ template: '<h1>Segunda</h1>' })
class Second {}

@Component({ imports: [RouterOutlet], template: '<router-outlet />' })
class Shell {
  constructor() {
    focusHeadingOnNavigation();
  }
}

describe('focusHeadingOnNavigation', () => {
  it('Navigate_WhenTheScreenChanges_MovesFocusToTheNewHeading', async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'primera', component: First },
          { path: 'segunda', component: Second },
        ]),
      ],
    });

    const fixture = TestBed.createComponent(Shell);
    const router = TestBed.inject(Router);

    await router.navigate(['/primera']);
    await fixture.whenStable();

    await router.navigate(['/segunda']);
    await fixture.whenStable();

    // Someone on a keyboard or a screen reader is now on the new screen's
    // heading, instead of still sitting on whatever they last touched.
    expect(document.activeElement?.textContent).toBe('Segunda');
  });
});
