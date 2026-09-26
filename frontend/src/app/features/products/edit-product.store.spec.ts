import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { ProblemTypes } from '../../core/api/problem-types';
import { EditProductStore } from './edit-product.store';
import type { ProductChanges } from './edit-product.store';
import { ProductsService } from './products.service';
import type { Product } from './products.service';

const ginTonic: Product = {
  id: 'id-1',
  name: 'Gin Tonic',
  description: 'Gin, tónica y una rodaja de lima.',
  imageUrl: null,
  price: 4500,
  stock: 20,
  categoryId: 'category-drinks',
  isAvailable: true,
  isSoldOut: false,
  isActive: true,
};

const onlyTheCorrection: ProductChanges = {
  correction: {
    name: 'Gin Tonic Doble',
    description: null,
    price: 5200,
    categoryId: 'category-drinks',
  },
  photo: null,
  stockChange: 0,
  isAvailable: null,
};

const rejectedWith = (status: number, type = '') =>
  vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status, error: { type } })));

describe('EditProductStore', () => {
  let store: EditProductStore;
  let products: Record<string, ReturnType<typeof vi.fn>>;

  function open(overrides: Record<string, ReturnType<typeof vi.fn>> = {}) {
    products = {
      update: vi.fn().mockReturnValue(of({ ...ginTonic, name: 'Gin Tonic Doble', price: 5200 })),
      uploadImage: vi.fn().mockReturnValue(of({ imageUrl: 'https://images.example.com/new.png' })),
      adjustStock: vi.fn().mockReturnValue(of({ ...ginTonic, stock: 32 })),
      markAvailable: vi.fn().mockReturnValue(of(ginTonic)),
      markUnavailable: vi.fn().mockReturnValue(of({ ...ginTonic, isAvailable: false })),
      deactivate: vi.fn().mockReturnValue(of({ ...ginTonic, isActive: false })),
      ...overrides,
    };

    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: ':venueSlug/staff/products', children: [] }]),
        EditProductStore,
        { provide: ProductsService, useValue: products },
      ],
    });

    store = TestBed.inject(EditProductStore);
  }

  // The upload answers with the address alone. Keeping what the correction
  // returned is what stops the new name from flickering back to the old one.
  it('keeps the correction when the photo goes up after it', () => {
    open();

    store.save('bar-alfa', 'id-1', {
      ...onlyTheCorrection,
      photo: new File([new Uint8Array(4)], 'new.png', { type: 'image/png' }),
    });

    expect(store.updated()).toEqual({
      ...ginTonic,
      name: 'Gin Tonic Doble',
      price: 5200,
      imageUrl: 'https://images.example.com/new.png',
    });
  });

  it('says nothing was saved when the correction itself fails', () => {
    open({ update: rejectedWith(0) });

    store.save('bar-alfa', 'id-1', { ...onlyTheCorrection, stockChange: 12 });

    expect(store.status()).toBe('unreachable');
    expect(products['adjustStock']).not.toHaveBeenCalled();
  });

  it('tells a taken name apart from any other failure', () => {
    open({ update: rejectedWith(409, ProblemTypes.productNameTaken) });

    store.save('bar-alfa', 'id-1', onlyTheCorrection);

    expect(store.status()).toBe('nameTaken');
  });

  it('says part of it was saved when a later step fails', () => {
    open({ adjustStock: rejectedWith(0) });

    store.save('bar-alfa', 'id-1', { ...onlyTheCorrection, stockChange: 12 });

    expect(store.status()).toBe('partial');
    expect(store.stockAdjustments()).toBe(0);
  });

  // Sales left less than the correction takes away. Worth its own answer:
  // looking at the stock again is the fix, not trying the same save.
  it('tells sales made meanwhile apart from any other failure of the stock', () => {
    open({ adjustStock: rejectedWith(409, ProblemTypes.productStockMoved) });

    store.save('bar-alfa', 'id-1', { ...onlyTheCorrection, stockChange: -18 });

    expect(store.status()).toBe('stockMoved');
  });

  it('counts the adjustments that reached the API', () => {
    open({ markUnavailable: rejectedWith(0) });

    store.save('bar-alfa', 'id-1', { ...onlyTheCorrection, stockChange: 12, isAvailable: false });

    expect(store.stockAdjustments()).toBe(1);
  });

  it('does not start a second save while the first is in flight', () => {
    open({ update: vi.fn().mockReturnValue(new Subject<Product>()) });

    store.save('bar-alfa', 'id-1', onlyTheCorrection);
    store.save('bar-alfa', 'id-1', onlyTheCorrection);

    expect(products['update']).toHaveBeenCalledTimes(1);
    expect(store.isBusy()).toBe(true);
  });

  it('shows the product as the API left it after taking it off the menu', () => {
    open();

    store.deactivate('id-1');

    expect(store.updated()?.isActive).toBe(false);
    expect(store.accessStatus()).toBe('idle');
  });

  it('says so when it could not be taken off the menu', () => {
    open({ deactivate: rejectedWith(0) });

    store.deactivate('id-1');

    expect(store.accessStatus()).toBe('unreachable');
  });
});
