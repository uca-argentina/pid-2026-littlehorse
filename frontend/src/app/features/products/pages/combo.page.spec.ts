import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { fireEvent, render, screen } from '@testing-library/angular';
import { of, throwError } from 'rxjs';
import { ProblemTypes } from '../../../core/api/problem-types';
import { CATEGORIES_URL } from '../../categories/categories.service';
import { PRODUCTS_URL, ProductsService } from '../products.service';
import type { Product } from '../products.service';
import { ComboPage } from './combo.page';

const noAudit = { createdAt: null, createdBy: null, lastModifiedAt: null, lastModifiedBy: null };

function aProduct(overrides: Partial<Product>): Product {
  return {
    id: 'id',
    name: 'Producto',
    description: null,
    imageUrl: null,
    price: 1000,
    stock: 10,
    categoryId: 'drinks',
    isAvailable: true,
    isSoldOut: false,
    isActive: true,
    audit: noAudit,
    isCombo: false,
    components: [],
    ...overrides,
  };
}

const fernet = aProduct({ id: 'fernet', name: 'Fernet', price: 4000 });
const ice = aProduct({ id: 'ice', name: 'Hielo', price: 500 });
const gone = aProduct({ id: 'gone', name: 'Dado de baja', isActive: false });
const combo = aProduct({
  id: 'combo',
  name: 'Combo Fernet',
  price: 8000,
  stock: 0,
  isCombo: true,
  components: [
    { productId: 'fernet', quantity: 2 },
    { productId: 'ice', quantity: 1 },
  ],
});

let settle: () => Promise<void>;

async function openScreen(id?: string, service: Partial<ProductsService> = {}) {
  const { fixture } = await render(ComboPage, {
    inputs: { venueSlug: 'bar-alfa', ...(id === undefined ? {} : { id }) },
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideRouter([{ path: ':venueSlug/staff/products', children: [] }]),
      { provide: ProductsService, useValue: service },
    ],
  });

  const http = TestBed.inject(HttpTestingController);

  http.expectOne(PRODUCTS_URL).flush([fernet, ice, gone, combo]);
  http.expectOne(CATEGORIES_URL).flush([{ id: 'drinks', name: 'Tragos' }]);
  settle = async () => {
    await fixture.whenStable();
    fixture.detectChanges();
  };
  await TestBed.inject(Router).navigateByUrl('/');
  await settle();
}

async function addPart(productName: RegExp, quantity: string): Promise<void> {
  const option = screen.getByRole('option', { name: productName }) as HTMLOptionElement;

  fireEvent.change(screen.getByLabelText(/^producto$/i), { target: { value: option.value } });
  fireEvent.input(screen.getByLabelText(/^cantidad$/i), { target: { value: quantity } });
  fireEvent.click(screen.getByRole('button', { name: /^agregar$/i }));
  await settle();
}

function fillData(): void {
  fireEvent.input(screen.getByLabelText(/^nombre/i), { target: { value: 'Balde' } });
  fireEvent.input(screen.getByLabelText(/precio del combo/i), { target: { value: '9000' } });
  fireEvent.change(screen.getByLabelText(/categoría/i), { target: { value: 'drinks' } });
}

describe('ComboPage', () => {
  it('offers only products on the menu that are not combos', async () => {
    await openScreen();

    expect(screen.getByRole('option', { name: 'Fernet' })).toBeTruthy();
    expect(screen.queryByRole('option', { name: 'Dado de baja' })).toBeNull();
    expect(screen.queryByRole('option', { name: 'Combo Fernet' })).toBeNull();
  });

  it('creates the combo with its products and goes back to the listing', async () => {
    const createCombo = vi.fn().mockReturnValue(of(combo));
    await openScreen(undefined, { createCombo });

    fillData();
    await addPart(/fernet/i, '2');
    await addPart(/hielo/i, '1');
    fireEvent.click(screen.getByRole('button', { name: /guardar combo/i }));

    expect(createCombo).toHaveBeenCalledWith({
      name: 'Balde',
      description: null,
      imageUrl: null,
      price: 9000,
      categoryId: 'drinks',
      components: [
        { productId: 'fernet', quantity: 2 },
        { productId: 'ice', quantity: 1 },
      ],
    });
  });

  it('shows what it would cost bought separately', async () => {
    await openScreen();

    await addPart(/fernet/i, '2');

    expect(screen.getByText(/por separado cuesta \$ 8\.000,00/i)).toBeTruthy();
  });

  it('stops a combo of a single unit before sending it', async () => {
    const createCombo = vi.fn();
    await openScreen(undefined, { createCombo });

    fillData();
    await addPart(/fernet/i, '1');
    fireEvent.click(screen.getByRole('button', { name: /guardar combo/i }));

    expect(createCombo).not.toHaveBeenCalled();
    expect(screen.getByRole('alert').textContent).toMatch(/al menos 2 unidades/i);
  });

  it('says so when the name is taken', async () => {
    const createCombo = vi.fn().mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 409,
            error: { type: ProblemTypes.productNameTaken },
          }),
      ),
    );
    await openScreen(undefined, { createCombo });

    fillData();
    await addPart(/fernet/i, '2');
    fireEvent.click(screen.getByRole('button', { name: /guardar combo/i }));

    expect(screen.getByRole('alert').textContent).toMatch(/ya hay un producto con ese nombre/i);
  });

  it('opens an existing combo filled in, and saves its data and its products', async () => {
    const update = vi.fn().mockReturnValue(of(combo));
    const replaceComponents = vi.fn().mockReturnValue(of(combo));
    await openScreen('combo', { update, replaceComponents });

    expect((screen.getByLabelText(/^nombre/i) as HTMLInputElement).value).toBe('Combo Fernet');
    expect(screen.getByRole('cell', { name: 'Hielo' })).toBeTruthy();

    fireEvent.click(screen.getByRole('button', { name: /sacar hielo/i }));
    fireEvent.click(screen.getByRole('button', { name: /guardar combo/i }));

    expect(update).toHaveBeenCalledWith('combo', expect.objectContaining({ name: 'Combo Fernet' }));
    expect(replaceComponents).toHaveBeenCalledWith('combo', [{ productId: 'fernet', quantity: 2 }]);
  });
});
