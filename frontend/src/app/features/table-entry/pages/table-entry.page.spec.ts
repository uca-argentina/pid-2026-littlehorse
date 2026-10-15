import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { render, screen } from '@testing-library/angular';
import { ProblemTypes } from '../../../core/api/problem-types';
import { BrowserStore } from '../../../core/storage/browser-store';
import { StoreInMemory } from '../../../core/storage/store-in-memory';
import { VipTables, vipTableUrl } from '../../../core/vip-table/vip-tables';
import { TableEntryPage } from './table-entry.page';

const CODE = 'a'.repeat(32);

async function openTheQr() {
  const rendered = await render(TableEntryPage, {
    inputs: { venueSlug: 'bar-alfa', code: CODE },
    providers: [
      provideRouter([{ path: ':venueSlug/menu', children: [] }]),
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: BrowserStore, useValue: new StoreInMemory() },
    ],
  });

  return {
    rendered,
    http: TestBed.inject(HttpTestingController),
    tables: TestBed.inject(VipTables),
  };
}

describe('TableEntryPage', () => {
  // US-41: the waiter's QR opens the menu of that table.
  it('remembers the table and opens its menu when the code works tonight', async () => {
    const { rendered, http, tables } = await openTheQr();

    http.expectOne(vipTableUrl('bar-alfa', CODE)).flush({ tableNumber: 5, balance: 150000 });
    await rendered.fixture.whenStable();

    expect(tables.codeFor('bar-alfa')()).toBe(CODE);
    expect(TestBed.inject(Router).url).toBe('/bar-alfa/menu');
  });

  // Criterion 4: ran out, regenerated or invented — the app says so and
  // offers to order the normal way.
  it('says the code no longer works, and offers to order for the bar', async () => {
    const { rendered, http, tables } = await openTheQr();

    http
      .expectOne(vipTableUrl('bar-alfa', CODE))
      .flush({ type: ProblemTypes.vipAccountNotFound }, { status: 404, statusText: 'Not Found' });
    await rendered.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toContain('Este código ya no sirve');
    expect(tables.codeFor('bar-alfa')()).toBeNull();

    screen.getByRole('button', { name: /pedir para retirar en la barra/i }).click();
    await rendered.fixture.whenStable();

    expect(TestBed.inject(Router).url).toBe('/bar-alfa/menu');
  });

  it('says so when the signal fails, and tries again', async () => {
    const { rendered, http } = await openTheQr();

    http.expectOne(vipTableUrl('bar-alfa', CODE)).error(new ProgressEvent('error'));
    await rendered.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toContain('Fijate la señal');

    screen.getByRole('button', { name: /probar de nuevo/i }).click();
    TestBed.tick();

    http.expectOne(vipTableUrl('bar-alfa', CODE)).flush({ tableNumber: 5, balance: 150000 });
    await rendered.fixture.whenStable();

    expect(TestBed.inject(Router).url).toBe('/bar-alfa/menu');
  });
});
