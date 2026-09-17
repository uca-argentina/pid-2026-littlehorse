import { TestBed } from '@angular/core/testing';
import { BrowserStore } from './browser-store';

/**
 * The seam exists for the cases where the browser store is not there to answer,
 * and the test runner is one of them: this is Node, where `localStorage` is
 * undefined. Every assertion below is that situation, not a simulation of it.
 */
describe('BrowserStore', () => {
  function aStore(): BrowserStore {
    TestBed.resetTestingModule();

    return TestBed.inject(BrowserStore);
  }

  it('reads nothing when there is no browser store to read from', () => {
    expect(aStore().read('drinkit.whatever')).toBeNull();
  });

  // A private window, a browser blocking site data, a full quota. None of them
  // are worth taking the screen down for.
  it('does not throw when there is nowhere to write', () => {
    expect(() => aStore().write('drinkit.whatever', 'algo')).not.toThrow();
  });
});
