import { computed } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { BrowserStore } from '../storage/browser-store';
import { StoreInMemory } from '../storage/store-in-memory';
import { VipTables, vipTableUrl } from './vip-tables';

describe('VipTables', () => {
  let tables: VipTables;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [{ provide: BrowserStore, useValue: new StoreInMemory() }],
    });
    tables = TestBed.inject(VipTables);
  });

  it('has no table until a QR opens one', () => {
    expect(tables.codeOf('bar-alfa')).toBeNull();
  });

  it('remembers the table a QR opened, for that venue only', () => {
    tables.remember('bar-alfa', 'the-code');

    expect(tables.codeOf('bar-alfa')).toBe('the-code');
    expect(tables.codeOf('bar-beta')).toBeNull();
  });

  // "Pedir para retirar en la barra": the phone stops ordering for the table.
  it('forgets the table when asked, and says so to whoever was reading it', () => {
    const code = TestBed.runInInjectionContext(() => computed(() => tables.codeOf('bar-alfa')));
    tables.remember('bar-alfa', 'the-code');
    expect(code()).toBe('the-code');

    tables.forget('bar-alfa');

    expect(code()).toBeNull();
  });

  // Found in review: a tampered QR could steer the request somewhere else.
  it('keeps whatever a QR says inside its own segment of the address', () => {
    expect(vipTableUrl('bar-alfa', '../../staff/tables?x=1#y')).toBe(
      '/api/bar-alfa/vip-accounts/..%2F..%2Fstaff%2Ftables%3Fx%3D1%23y',
    );
  });
});
