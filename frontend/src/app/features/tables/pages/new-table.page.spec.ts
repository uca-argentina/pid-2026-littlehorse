import { HttpErrorResponse } from '@angular/common/http';
import { Router, provideRouter } from '@angular/router';
import { TestBed } from '@angular/core/testing';
import { fireEvent, render, screen } from '@testing-library/angular';
import { Subject, of, throwError } from 'rxjs';
import { ProblemTypes } from '../../../core/api/problem-types';
import { NewTableStore } from '../new-table.store';
import { TablesService } from '../tables.service';
import type { VipTable } from '../tables.service';
import { NewTablePage } from './new-table.page';

const noAudit = { createdAt: null, createdBy: null, lastModifiedAt: null, lastModifiedBy: null };

const created: VipTable = {
  id: 'id-1',
  number: 5,
  capacity: 8,
  location: null,
  isActive: true,
  audit: noAudit,
};

const rejectedWith = (status: number, type: string) =>
  vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status, error: { type } })));

function openScreen(create = vi.fn().mockReturnValue(of(created))) {
  return render(NewTablePage, {
    inputs: { venueSlug: 'bar-alfa' },
    providers: [
      // The screen navigates back to the listing for real: without a route to
      // land on, the router rejects and that masks the actual assertion.
      provideRouter([{ path: ':venueSlug/staff/tables', children: [] }]),
      NewTableStore,
      { provide: TablesService, useValue: { create } },
    ],
  }).then((rendered) => ({ rendered, create }));
}

function numberField(): HTMLInputElement {
  return screen.getByLabelText(/^número/i) as HTMLInputElement;
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

/** Types the number with a valid capacity, so the case is only about the number. */
function type(value: string): void {
  fill(numberField(), value);
  fill(capacityField(), '8');
}

function save(): void {
  screen.getByRole('button', { name: /crear mesa/i }).click();
}

describe('NewTablePage', () => {
  it('sends the number, the people and the location that were typed', async () => {
    const { create } = await openScreen();

    fill(numberField(), ' 5 ');
    fill(capacityField(), ' 10 ');
    fill(locationField(), '  Al lado de la cabina  ');
    save();

    expect(create).toHaveBeenCalledWith({
      number: 5,
      capacity: 10,
      location: 'Al lado de la cabina',
    });
  });

  it('sends no location when it was left blank', async () => {
    const { create } = await openScreen();

    type('5');
    fill(locationField(), '   ');
    save();

    expect(create).toHaveBeenCalledWith({ number: 5, capacity: 8, location: null });
  });

  it.each([
    ['no people', ''],
    ['zero people', '0'],
    ['more people than a table seats', '51'],
    ['people with decimals', '4.5'],
  ])('refuses to save with %s', async (_case, value) => {
    const { create } = await openScreen();

    type('5');
    fill(capacityField(), value);
    save();

    expect(create).not.toHaveBeenCalled();
  });

  it('says how many people a table can seat, next to the people', async () => {
    const { rendered } = await openScreen();

    fill(numberField(), '5');
    save();
    await rendered.fixture.whenStable();

    expect(screen.getByText(/entre 1 y 50 personas/i)).not.toBeNull();
    expect(capacityField().getAttribute('aria-invalid')).toBe('true');
  });

  it('refuses a location longer than the listing can show', async () => {
    const { create } = await openScreen();

    type('5');
    fill(locationField(), 'a'.repeat(61));
    save();

    expect(create).not.toHaveBeenCalled();
  });

  it('goes back to the listing once the table exists', async () => {
    const { rendered } = await openScreen();

    type('5');
    save();
    await rendered.fixture.whenStable();

    expect(TestBed.inject(Router).url).toBe('/bar-alfa/staff/tables');
  });

  it.each([
    ['no number', ''],
    ['only spaces', '   '],
    ['zero', '0'],
    ['a negative number', '-3'],
    ['a number above the limit', '1000'],
    ['a number with decimals', '5.5'],
    ['something that is not a number', 'cinco'],
  ])('refuses to save with %s', async (_case, value) => {
    const { create } = await openScreen();

    type(value);
    save();

    expect(create).not.toHaveBeenCalled();
  });

  it('says what the number has to be, next to the number', async () => {
    const { rendered } = await openScreen();

    save();
    await rendered.fixture.whenStable();

    expect(screen.getByText(/entre 1 y 999/i)).not.toBeNull();
    expect(numberField().getAttribute('aria-invalid')).toBe('true');
  });

  it('says the number already belongs to a table of this venue', async () => {
    const { rendered } = await openScreen(rejectedWith(409, ProblemTypes.tableNumberTaken));

    type('5');
    save();
    await rendered.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toContain('Ya hay una mesa con ese número');
  });

  // Retyping after a rejection is what makes people give up on a form.
  it('keeps what was typed after the number is rejected', async () => {
    const { rendered } = await openScreen(rejectedWith(409, ProblemTypes.tableNumberTaken));

    type('5');
    save();
    await rendered.fixture.whenStable();

    expect(numberField().value).toBe('5');
  });

  it('says so when the API cannot be reached', async () => {
    const { rendered } = await openScreen(rejectedWith(0, ''));

    type('5');
    save();
    await rendered.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toContain('No pudimos');
  });

  it('cannot be submitted twice while the first attempt is in flight', async () => {
    const { rendered, create } = await openScreen(vi.fn().mockReturnValue(new Subject<VipTable>()));

    type('5');
    save();
    await rendered.fixture.whenStable();

    const button = screen.getByRole('button', { name: /creando/i }) as HTMLButtonElement;
    button.click();

    expect(create).toHaveBeenCalledTimes(1);
    expect(button.disabled).toBe(true);
  });

  it('offers a way back to the listing without saving', async () => {
    await openScreen();

    expect(screen.getByRole('link', { name: /cancelar/i }).getAttribute('href')).toBe(
      '/bar-alfa/staff/tables',
    );
  });
});
