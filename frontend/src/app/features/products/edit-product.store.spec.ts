import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';
import { ProblemTypes } from '../../core/api/problem-types';
import { EditProductStore } from './edit-product.store';
import { ProductsService } from './products.service';
import type { Product } from './products.service';

const ginTonic: Product = {
  id: 'id-1',
  name: 'Gin Tonic',
  description: 'Gin, tónica y una rodaja de lima.',
  imageUrl: null,
  price: 4500,
  stock: 20,
  isAvailable: true,
  isSoldOut: false,
  isActive: true,
};

const rejectedWith = (status: number, type: string) =>
  vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status, error: { type } })));

describe('EditProductStore', () => {
  let store: EditProductStore;
  let products: Record<string, ReturnType<typeof vi.fn>>;

  function open(overrides: Record<string, ReturnType<typeof vi.fn>> = {}) {
    products = {
      update: vi.fn().mockReturnValue(of({ ...ginTonic, name: 'Fernet con Coca', price: 3800 })),
      deactivate: vi.fn().mockReturnValue(of({ ...ginTonic, isActive: false })),
      ...overrides,
    };

    TestBed.configureTestingModule({
      providers: [EditProductStore, { provide: ProductsService, useValue: products }],
    });

    store = TestBed.inject(EditProductStore);
  }

  describe('correcting the details', () => {
    it('sends what was corrected', () => {
      open();

      store.update('id-1', { name: 'Fernet con Coca', description: null, price: 3800 });

      expect(products['update']).toHaveBeenCalledWith('id-1', {
        name: 'Fernet con Coca',
        description: null,
        price: 3800,
      });
    });

    it('says it worked, so the screen can confirm it', () => {
      open();

      store.update('id-1', { name: 'Fernet con Coca', description: null, price: 3800 });

      expect(store.detailsStatus()).toBe('saved');
    });

    it('hands back what the API answered, so the screen shows the new data', () => {
      open();

      store.update('id-1', { name: 'Fernet con Coca', description: null, price: 3800 });

      expect(store.updated()?.name).toBe('Fernet con Coca');
    });

    // Two taps on a slow connection must not send the same correction twice.
    it('does not send twice while a request is in flight', () => {
      open({ update: vi.fn().mockReturnValue(new Subject<Product>()) });

      store.update('id-1', { name: 'Fernet con Coca', description: null, price: 3800 });
      store.update('id-1', { name: 'Fernet con Coca', description: null, price: 3800 });

      expect(products['update']).toHaveBeenCalledTimes(1);
    });

    it('says the name is taken when the API refuses it', () => {
      open({ update: rejectedWith(409, ProblemTypes.productNameTaken) });

      store.update('id-1', { name: 'Fernet con Coca', description: null, price: 3800 });

      expect(store.detailsStatus()).toBe('nameTaken');
    });

    it('falls back to a single failure for anything else', () => {
      open({ update: rejectedWith(500, 'about:blank') });

      store.update('id-1', { name: 'Fernet con Coca', description: null, price: 3800 });

      expect(store.detailsStatus()).toBe('unreachable');
    });
  });

  describe('taking it off the menu', () => {
    it('deactivates the product that was picked', () => {
      open();

      store.deactivate('id-1');

      expect(products['deactivate']).toHaveBeenCalledWith('id-1');
      expect(store.accessStatus()).toBe('saved');
    });

    it('hands back what the API answered, so the screen shows it deactivated', () => {
      open();

      store.deactivate('id-1');

      expect(store.updated()?.isActive).toBe(false);
    });

    it('falls back to a single failure for anything else', () => {
      open({ deactivate: rejectedWith(500, 'about:blank') });

      store.deactivate('id-1');

      expect(store.accessStatus()).toBe('unreachable');
    });
  });

  // Each action reports on its own. A failed correction must not make the
  // deactivation next to it look like it failed too.
  it('keeps the two outcomes apart', () => {
    open({ update: rejectedWith(409, ProblemTypes.productNameTaken) });

    store.update('id-1', { name: 'Fernet con Coca', description: null, price: 3800 });

    expect(store.detailsStatus()).toBe('nameTaken');
    expect(store.accessStatus()).toBe('idle');
  });
});
