import { HttpErrorResponse } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { fireEvent, render, screen } from '@testing-library/angular';
import { Subject, of, throwError } from 'rxjs';
import { ProblemTypes } from '../../../core/api/problem-types';
import { NewProductStore } from '../new-product.store';
import { ProductsService } from '../products.service';
import type { Product } from '../products.service';
import { NewProductPage } from './new-product.page';

const created: Product = {
  id: 'id-1',
  name: 'Gin Tonic',
  description: null,
  imageUrl: null,
  price: 4500,
  stock: 20,
  isAvailable: true,
  isSoldOut: false,
  isActive: true,
};

const rejectedWith = (status: number, type: string) =>
  vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status, error: { type } })));

function openScreen(create = vi.fn().mockReturnValue(of(created))) {
  return render(NewProductPage, {
    inputs: { venueSlug: 'bar-alfa' },
    providers: [
      // The screen navigates back to the listing for real: without a route to
      // land on, the router rejects and that masks the actual assertion.
      provideRouter([{ path: ':venueSlug/staff/products', children: [] }]),
      NewProductStore,
      { provide: ProductsService, useValue: { create } },
    ],
  }).then((rendered) => ({ rendered, create }));
}

function field(label: RegExp): HTMLInputElement {
  return screen.getByLabelText(label) as HTMLInputElement;
}

function type(label: RegExp, value: string): void {
  fireEvent.input(field(label), { target: { value } });
}

function fillAGinTonic(): void {
  type(/^nombre/i, 'Gin Tonic');
  type(/descripción/i, 'Gin, tónica y una rodaja de lima.');
  type(/precio/i, '4500');
  type(/stock/i, '20');
}

function save(): void {
  screen.getByRole('button', { name: /crear producto/i }).click();
}

describe('NewProductPage', () => {
  it('sends what was typed, with the name trimmed and the numbers as numbers', async () => {
    const { create } = await openScreen();

    type(/^nombre/i, '  Gin Tonic  ');
    type(/descripción/i, 'Gin, tónica y una rodaja de lima.');
    type(/precio/i, '4500');
    type(/stock/i, '20');
    save();

    expect(create).toHaveBeenCalledWith({
      name: 'Gin Tonic',
      description: 'Gin, tónica y una rodaja de lima.',
      imageUrl: null,
      price: 4500,
      stock: 20,
    });
  });

  // The description is optional and the API takes null, not an empty string:
  // the listing branches on null to leave the line out.
  it('sends no description as null', async () => {
    const { create } = await openScreen();

    type(/^nombre/i, 'Gin Tonic');
    type(/precio/i, '4500');
    type(/stock/i, '20');
    save();

    expect(create).toHaveBeenCalledWith(expect.objectContaining({ description: null }));
  });

  // US-06, criteria 2 and 3. Caught here and not by the API: the venue's
  // connection is the slowest part of this screen.
  it.each([
    ['no name', '', '4500', '20'],
    ['a name that is only spaces', '   ', '4500', '20'],
    ['no price', 'Gin Tonic', '', '20'],
    ['a price of zero', 'Gin Tonic', '0', '20'],
    ['a negative price', 'Gin Tonic', '-1', '20'],
    ['no stock', 'Gin Tonic', '4500', ''],
    ['a negative stock', 'Gin Tonic', '4500', '-1'],
    ['a stock that is not a whole number', 'Gin Tonic', '4500', '1.5'],
  ])('refuses to save with %s', async (_case, name, price, stock) => {
    const { create } = await openScreen();

    type(/^nombre/i, name);
    type(/precio/i, price);
    type(/stock/i, stock);
    save();

    expect(create).not.toHaveBeenCalled();
  });

  // Criterion 3: the form points at the field, it does not just refuse.
  it('says the name is missing, next to the name', async () => {
    const { rendered } = await openScreen();

    type(/precio/i, '4500');
    type(/stock/i, '20');
    save();
    await rendered.fixture.whenStable();

    expect(screen.getByText(/ponele un nombre/i)).not.toBeNull();
    expect(field(/^nombre/i).getAttribute('aria-invalid')).toBe('true');
  });

  // Criterion 2: "no me deja y me dice por qué".
  it('says the price has to be above zero, next to the price', async () => {
    const { rendered } = await openScreen();

    type(/^nombre/i, 'Gin Tonic');
    type(/precio/i, '0');
    type(/stock/i, '20');
    save();
    await rendered.fixture.whenStable();

    expect(screen.getByText(/mayor a cero/i)).not.toBeNull();
    expect(field(/precio/i).getAttribute('aria-invalid')).toBe('true');
  });

  it('says the stock has to be a whole number of zero or more', async () => {
    const { rendered } = await openScreen();

    type(/^nombre/i, 'Gin Tonic');
    type(/precio/i, '4500');
    type(/stock/i, '-1');
    save();
    await rendered.fixture.whenStable();

    expect(screen.getByText(/cero o más/i)).not.toBeNull();
  });

  // Stock at zero is allowed: a product can be loaded before the delivery
  // arrives, and it shows as sold out until then.
  it('accepts a stock of zero', async () => {
    const { create } = await openScreen();

    type(/^nombre/i, 'Gin Tonic');
    type(/precio/i, '4500');
    type(/stock/i, '0');
    save();

    expect(create).toHaveBeenCalledWith(expect.objectContaining({ stock: 0 }));
  });

  it('clears the message of the field that was fixed and keeps the other', async () => {
    const { rendered } = await openScreen();

    type(/precio/i, '0');
    type(/stock/i, '20');
    save();
    await rendered.fixture.whenStable();

    type(/^nombre/i, 'Gin Tonic');
    await rendered.fixture.whenStable();

    expect(screen.queryByText(/ponele un nombre/i)).toBeNull();
    expect(screen.getByText(/mayor a cero/i)).not.toBeNull();
  });

  it('says the name is already used in this venue', async () => {
    const { rendered } = await openScreen(rejectedWith(409, ProblemTypes.productNameTaken));

    fillAGinTonic();
    save();
    await rendered.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toContain('Ya hay un producto');
  });

  // Retyping the whole form after a rejection is what makes people give up on
  // a form. What they typed stays; only the name needs changing.
  it('keeps what was typed after the name is rejected', async () => {
    const { rendered } = await openScreen(rejectedWith(409, ProblemTypes.productNameTaken));

    fillAGinTonic();
    save();
    await rendered.fixture.whenStable();

    expect(field(/^nombre/i).value).toBe('Gin Tonic');
    expect(field(/precio/i).value).toBe('4500');
  });

  it('says so when the API cannot be reached', async () => {
    const { rendered } = await openScreen(rejectedWith(0, ''));

    fillAGinTonic();
    save();
    await rendered.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toContain('No pudimos');
  });

  it('cannot be submitted twice while the first attempt is in flight', async () => {
    const { rendered, create } = await openScreen(vi.fn().mockReturnValue(new Subject<Product>()));

    fillAGinTonic();
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
