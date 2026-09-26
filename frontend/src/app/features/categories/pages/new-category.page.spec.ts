import { HttpErrorResponse } from '@angular/common/http';
import { Router, provideRouter } from '@angular/router';
import { TestBed } from '@angular/core/testing';
import { fireEvent, render, screen } from '@testing-library/angular';
import { Subject, of, throwError } from 'rxjs';
import { ProblemTypes } from '../../../core/api/problem-types';
import { CategoriesService } from '../categories.service';
import type { Category } from '../categories.service';
import { NewCategoryStore } from '../new-category.store';
import { NewCategoryPage } from './new-category.page';

const created: Category = { id: 'id-1', name: 'Cervezas' };

const rejectedWith = (status: number, type: string) =>
  vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status, error: { type } })));

function openScreen(create = vi.fn().mockReturnValue(of(created))) {
  return render(NewCategoryPage, {
    inputs: { venueSlug: 'bar-alfa' },
    providers: [
      // The screen navigates back to the listing for real: without a route to
      // land on, the router rejects and that masks the actual assertion.
      provideRouter([{ path: ':venueSlug/staff/products', children: [] }]),
      NewCategoryStore,
      { provide: CategoriesService, useValue: { create } },
    ],
  }).then((rendered) => ({ rendered, create }));
}

function nameField(): HTMLInputElement {
  return screen.getByLabelText(/^nombre/i) as HTMLInputElement;
}

function type(value: string): void {
  fireEvent.input(nameField(), { target: { value } });
}

function save(): void {
  screen.getByRole('button', { name: /crear categoría/i }).click();
}

describe('NewCategoryPage', () => {
  it('sends the name that was typed, trimmed', async () => {
    const { create } = await openScreen();

    type('  Cervezas  ');
    save();

    expect(create).toHaveBeenCalledWith({ name: 'Cervezas' });
  });

  it('goes back to the listing once the category exists', async () => {
    const { rendered } = await openScreen();

    type('Cervezas');
    save();
    await rendered.fixture.whenStable();

    expect(TestBed.inject(Router).url).toBe('/bar-alfa/staff/products');
  });

  it.each([
    ['no name', ''],
    ['a name that is only spaces', '   '],
    ['a name that is too long', 'a'.repeat(41)],
  ])('refuses to save with %s', async (_case, name) => {
    const { create } = await openScreen();

    type(name);
    save();

    expect(create).not.toHaveBeenCalled();
  });

  it('says the name is missing, next to the name', async () => {
    const { rendered } = await openScreen();

    save();
    await rendered.fixture.whenStable();

    expect(screen.getByText(/ponele un nombre/i)).not.toBeNull();
    expect(nameField().getAttribute('aria-invalid')).toBe('true');
  });

  it('says the name is already used in this venue', async () => {
    const { rendered } = await openScreen(rejectedWith(409, ProblemTypes.categoryNameTaken));

    type('Cervezas');
    save();
    await rendered.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toContain('Ya hay una categoría');
  });

  // Retyping after a rejection is what makes people give up on a form.
  it('keeps what was typed after the name is rejected', async () => {
    const { rendered } = await openScreen(rejectedWith(409, ProblemTypes.categoryNameTaken));

    type('Cervezas');
    save();
    await rendered.fixture.whenStable();

    expect(nameField().value).toBe('Cervezas');
  });

  it('says so when the API cannot be reached', async () => {
    const { rendered } = await openScreen(rejectedWith(0, ''));

    type('Cervezas');
    save();
    await rendered.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toContain('No pudimos');
  });

  it('cannot be submitted twice while the first attempt is in flight', async () => {
    const { rendered, create } = await openScreen(vi.fn().mockReturnValue(new Subject<Category>()));

    type('Cervezas');
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
      '/bar-alfa/staff/products',
    );
  });
});
