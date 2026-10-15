import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { render, screen } from '@testing-library/angular';
import { of, throwError } from 'rxjs';
import { ProblemTypes } from '../../../core/api/problem-types';
import { TABLES_URL, TablesService } from '../tables.service';
import type { VipTable } from '../tables.service';
import { TablesPage } from './tables.page';

const noAudit = { createdAt: null, createdBy: null, lastModifiedAt: null, lastModifiedBy: null };

const tableTwo: VipTable = {
  id: 'two',
  number: 2,
  capacity: 10,
  location: 'Al lado de la cabina',
  isActive: true,
  audit: noAudit,
};
const tableSeven: VipTable = {
  id: 'seven',
  number: 7,
  capacity: 1,
  location: null,
  isActive: false,
  audit: noAudit,
};

async function openScreen(service: Partial<Record<'deactivate' | 'reactivate', unknown>> = {}) {
  const rendered = await render(TablesPage, {
    inputs: { venueSlug: 'bar-alfa' },
    providers: [
      provideRouter([]),
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: TablesService, useValue: service },
    ],
  });

  return { rendered, http: TestBed.inject(HttpTestingController) };
}

/** Renders what the last answer changed, without waiting on any request still pending. */
async function settleOnce(fixture: { detectChanges: () => void }): Promise<void> {
  for (let round = 0; round < 3; round++) {
    fixture.detectChanges();
    TestBed.tick();
    await Promise.resolve();
  }
}

async function openScreenShowing(
  tables: VipTable[],
  service: Partial<Record<'deactivate' | 'reactivate', unknown>> = {},
) {
  const opened = await openScreen(service);

  opened.http.expectOne(TABLES_URL).flush(tables);
  await settleOnce(opened.rendered.fixture);

  return opened;
}

describe('TablesPage', () => {
  it('lists each table with its number, in the order the API sends them', async () => {
    await openScreenShowing([tableTwo, tableSeven]);

    const rows = screen.getAllByRole('listitem');

    expect(rows[0].textContent).toContain('Mesa 2');
    expect(rows[1].textContent).toContain('Mesa 7');
  });

  it('says how many people each table seats and where it is', async () => {
    await openScreenShowing([tableTwo, tableSeven]);

    const [withLocation, withoutLocation] = screen.getAllByRole('listitem');

    expect(withLocation.textContent).toContain('10 personas · Al lado de la cabina');
    expect(withoutLocation.textContent).toContain('1 persona');
    expect(withoutLocation.textContent).not.toContain('·');
  });

  it('says which tables are put away', async () => {
    await openScreenShowing([tableTwo, tableSeven]);

    const [active, away] = screen.getAllByRole('listitem');

    expect(active.textContent).not.toContain('Dada de baja');
    expect(away.textContent).toContain('Dada de baja');
  });

  it('offers a way to add a table', async () => {
    await openScreenShowing([tableTwo]);

    expect(screen.getByRole('link', { name: /nueva mesa/i }).getAttribute('href')).toBe(
      '/bar-alfa/staff/tables/new',
    );
  });

  it('says so when the venue has no tables yet', async () => {
    await openScreenShowing([]);

    expect(screen.getByRole('status').textContent).toContain('Todavía no hay mesas');
    expect(screen.queryAllByRole('listitem')).toHaveLength(0);
  });

  // The answer already is the updated table: asking for the whole listing again
  // costs a request over the venue's crowded signal and flashes "Cargando".
  it('puts a table away without loading the listing again', async () => {
    const deactivate = vi.fn().mockReturnValue(of({ ...tableTwo, isActive: false }));
    const { rendered, http } = await openScreenShowing([tableTwo], { deactivate });

    screen.getByRole('button', { name: /dar de baja la mesa 2/i }).click();
    await settleOnce(rendered.fixture);

    http.expectNone(TABLES_URL);
    expect(deactivate).toHaveBeenCalledWith('two');
    expect(screen.getByRole('listitem').textContent).toContain('Dada de baja');
  });

  it('brings a table back without loading the listing again', async () => {
    const reactivate = vi.fn().mockReturnValue(of({ ...tableSeven, isActive: true }));
    const { rendered, http } = await openScreenShowing([tableSeven], { reactivate });

    screen.getByRole('button', { name: /reactivar la mesa 7/i }).click();
    await settleOnce(rendered.fixture);

    http.expectNone(TABLES_URL);

    expect(reactivate).toHaveBeenCalledWith('seven');
    expect(screen.getByRole('listitem').textContent).not.toContain('Dada de baja');
  });

  it('offers to edit each table', async () => {
    await openScreenShowing([tableTwo, tableSeven]);

    expect(screen.getByRole('link', { name: /editar la mesa 2/i }).getAttribute('href')).toBe(
      '/bar-alfa/staff/tables/two',
    );
    expect(screen.getByRole('link', { name: /editar la mesa 7/i }).getAttribute('href')).toBe(
      '/bar-alfa/staff/tables/seven',
    );
  });

  it('offers only the change that makes sense for each table', async () => {
    await openScreenShowing([tableTwo, tableSeven]);

    expect(screen.queryByRole('button', { name: /reactivar la mesa 2/i })).toBeNull();
    expect(screen.queryByRole('button', { name: /dar de baja la mesa 7/i })).toBeNull();
  });

  it('says so when the change could not be made', async () => {
    const deactivate = vi
      .fn()
      .mockReturnValue(throwError(() => new HttpErrorResponse({ status: 0 })));
    const { rendered } = await openScreenShowing([tableTwo], { deactivate });

    screen.getByRole('button', { name: /dar de baja la mesa 2/i }).click();
    await settleOnce(rendered.fixture);

    expect(screen.getByRole('alert').textContent).toContain('No pudimos');
  });

  // Retrying never fixes a role that was taken away; telling them to check the
  // signal would have them tapping forever.
  it('says the account is no longer an administrator when a change is refused', async () => {
    const deactivate = vi
      .fn()
      .mockReturnValue(
        throwError(
          () => new HttpErrorResponse({ status: 403, error: { type: ProblemTypes.forbidden } }),
        ),
      );
    const { rendered } = await openScreenShowing([tableTwo], { deactivate });

    screen.getByRole('button', { name: /dar de baja la mesa 2/i }).click();
    await settleOnce(rendered.fixture);

    expect(screen.getByRole('alert').textContent).toContain('tu cuenta ya no lo es');
  });

  // US-39's edge: somebody bought the table for a night that has not ended.
  it('says the table is sold in a night that has not ended when the API refuses to put it away', async () => {
    const deactivate = vi.fn().mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 409,
            error: { type: ProblemTypes.tableInANightNotOver },
          }),
      ),
    );
    const { rendered } = await openScreenShowing([tableTwo], { deactivate });

    screen.getByRole('button', { name: /dar de baja la mesa 2/i }).click();
    await settleOnce(rendered.fixture);

    expect(screen.getByRole('alert').textContent).toContain('noche que todavía no terminó');
  });

  it('says so when the listing cannot be loaded', async () => {
    const { rendered, http } = await openScreen();

    http.expectOne(TABLES_URL).flush('', { status: 500, statusText: 'Server Error' });
    await rendered.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toContain('No pudimos traer las mesas');
  });

  it('says the account is no longer an administrator when the API refuses', async () => {
    const { rendered, http } = await openScreen();

    http
      .expectOne(TABLES_URL)
      .flush({ type: ProblemTypes.forbidden }, { status: 403, statusText: 'Forbidden' });
    await rendered.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toContain('tu cuenta ya no lo es');
  });
});
