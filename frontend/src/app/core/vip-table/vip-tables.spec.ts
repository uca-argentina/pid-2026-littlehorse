import { TestBed } from '@angular/core/testing';
import { BrowserStore } from '../storage/browser-store';
import { StoreInMemory } from '../storage/store-in-memory';
import { VipTables } from './vip-tables';

describe('VipTables', () => {
  let tables: VipTables;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [{ provide: BrowserStore, useValue: new StoreInMemory() }],
    });
    tables = TestBed.inject(VipTables);
  });

  it('has no table until a QR opens one', () => {
    expect(tables.codeFor('bar-alfa')()).toBeNull();
  });

  it('remembers the table a QR opened, for that venue only', () => {
    tables.remember('bar-alfa', 'the-code');

    expect(tables.codeFor('bar-alfa')()).toBe('the-code');
    expect(tables.codeFor('bar-beta')()).toBeNull();
  });

  // "Pedir para retirar en la barra": the phone stops ordering for the table.
  it('forgets the table when asked, and says so to whoever was reading it', () => {
    const code = tables.codeFor('bar-alfa');
    tables.remember('bar-alfa', 'the-code');

    tables.forget('bar-alfa');

    expect(code()).toBeNull();
  });
});
