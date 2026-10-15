import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { fireEvent, render, screen } from '@testing-library/angular';
import { of, throwError } from 'rxjs';
import { ProblemTypes } from '../../../core/api/problem-types';
import { TABLES_URL, TablesService } from '../tables.service';
import type { VipTable } from '../tables.service';
import { EditTablePage } from './edit-table.page';

const noAudit = { createdAt: null, createdBy: null, lastModifiedAt: null, lastModifiedBy: null };

const tableFive: VipTable = {
  id: 'five',
  number: 5,
  capacity: 8,
  location: 'Arriba',
  isActive: true,
  audit: noAudit,
};

async function openScreen(id: string, update: unknown = vi.fn().mockReturnValue(of(tableFive))) {
  const rendered = await render(EditTablePage, {
    inputs: { venueSlug: 'bar-alfa', id },
    providers: [
      // The screen navigates back to the listing for real: without a route to
      // land on, the router rejects and that masks the actual assertion.
      provideRouter([{ path: ':venueSlug/staff/tables', children: [] }]),
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: TablesService, useValue: { update } },
    ],
  });

  const http = TestBed.inject(HttpTestingController);

  return { rendered, http, update: update as ReturnType<typeof vi.fn> };
}

async function openScreenOn(id: string, update?: unknown) {
  const opened = await openScreen(id, update);

  opened.http.expectOne(TABLES_URL).flush([tableFive]);
  await opened.rendered.fixture.whenStable();

  return opened;
}

function capacityField(): HTMLInputElement {
  return screen.getByLabelText(/^personas/i) as HTMLInputElement;
}

function locationField(): HTMLInputElement {
  return screen.getByLabelText(/^ubicación/i) as HTMLInputElement;
}

function fill(field: HTMLInputElement, value: string): void {
  fireEvent.input(field, { target: { value } });
}

function save(): void {
  screen.getByRole('button', { name: /guardar/i }).click();
}

describe('EditTablePage', () => {
  it('shows the table with what it has now', async () => {
    await openScreenOn('five');

    expect(screen.getByRole('heading', { name: 'Mesa 5' })).not.toBeNull();
    expect(capacityField().value).toBe('8');
    expect(locationField().value).toBe('Arriba');
  });

  // The number is what the orders placed from the table say: it is shown, not offered.
  it('does not offer to change the number', async () => {
    await openScreenOn('five');

    expect(screen.queryByLabelText(/^número/i)).toBeNull();
  });

  it('sends the people and the location that were typed', async () => {
    const { update } = await openScreenOn('five');

    fill(capacityField(), ' 12 ');
    fill(locationField(), '  Al lado de la cabina  ');
    save();

    expect(update).toHaveBeenCalledWith('five', { capacity: 12, location: 'Al lado de la cabina' });
  });

  it('sends no location when it was emptied', async () => {
    const { update } = await openScreenOn('five');

    fill(locationField(), '');
    save();

    expect(update).toHaveBeenCalledWith('five', { capacity: 8, location: null });
  });

  it('goes back to the listing once saved', async () => {
    const { rendered } = await openScreenOn('five');

    save();
    await rendered.fixture.whenStable();

    expect(TestBed.inject(Router).url).toBe('/bar-alfa/staff/tables');
  });

  it('refuses to save with people out of range', async () => {
    const { rendered, update } = await openScreenOn('five');

    fill(capacityField(), '51');
    save();
    await rendered.fixture.whenStable();

    expect(update).not.toHaveBeenCalled();
    expect(screen.getByText(/entre 1 y 50 personas/i)).not.toBeNull();
  });

  it('says the table is not in this venue when the id is unknown', async () => {
    await openScreenOn('somebody-elses');

    expect(screen.getByRole('alert').textContent).toContain('No encontramos esa mesa');
    expect(screen.queryByRole('button', { name: /guardar/i })).toBeNull();
  });

  it('says so when the change could not be saved', async () => {
    const update = vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status: 0 })));
    const { rendered } = await openScreenOn('five', update);

    save();
    await rendered.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toContain('No pudimos guardar');
  });

  it('says the account is no longer an administrator when the API refuses', async () => {
    const update = vi
      .fn()
      .mockReturnValue(
        throwError(
          () => new HttpErrorResponse({ status: 403, error: { type: ProblemTypes.forbidden } }),
        ),
      );
    const { rendered } = await openScreenOn('five', update);

    save();
    await rendered.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toContain('tu cuenta ya no lo es');
  });

  it('offers a way back to the listing without saving', async () => {
    await openScreenOn('five');

    expect(screen.getByRole('link', { name: /cancelar/i }).getAttribute('href')).toBe(
      '/bar-alfa/staff/tables',
    );
  });
});
