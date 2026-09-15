import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { ProblemTypes } from '../../core/api/problem-types';
import { NewProductStore } from './new-product.store';
import { ProductsService } from './products.service';
import type { NewProduct, Product } from './products.service';

function rejectedWith(status: number, type: string): HttpErrorResponse {
  return new HttpErrorResponse({ status, error: { type } });
}

const created: Product = {
  id: '0199a0d2-0000-7000-8000-000000000001',
  name: 'Gin Tonic',
  description: null,
  imageUrl: null,
  price: 4500,
  stock: 20,
  isAvailable: true,
  isSoldOut: false,
  isActive: true,
};

const aNewProduct: NewProduct = {
  name: 'Gin Tonic',
  description: null,
  imageUrl: null,
  price: 4500,
  stock: 20,
};

describe('NewProductStore', () => {
  let store: NewProductStore;
  let create: ReturnType<typeof vi.fn>;
  let router: Router;

  beforeEach(() => {
    create = vi.fn();

    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        NewProductStore,
        { provide: ProductsService, useValue: { create } },
      ],
    });

    store = TestBed.inject(NewProductStore);
    router = TestBed.inject(Router);
  });

  // Two taps on a slow connection must not create the same product twice.
  it('does not send twice while a request is in flight', () => {
    create.mockReturnValue(new Subject<Product>());

    store.submit('bar-alfa', aNewProduct);
    store.submit('bar-alfa', aNewProduct);

    expect(create).toHaveBeenCalledTimes(1);
  });

  it('goes back to the listing once the product exists', () => {
    create.mockReturnValue(of(created));
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    store.submit('bar-alfa', aNewProduct);

    expect(navigate).toHaveBeenCalledWith(['bar-alfa', 'staff', 'products']);
    expect(store.status()).toBe('idle');
  });

  it('reports a name that this venue already uses', () => {
    create.mockReturnValue(throwError(() => rejectedWith(409, ProblemTypes.productNameTaken)));

    store.submit('bar-alfa', aNewProduct);

    expect(store.status()).toBe('nameTaken');
  });

  // Everything else — a dropped connection, a 500, a rule the form did not
  // catch — is the same thing from the screen's point of view: nothing was
  // created, try again.
  it.each([
    ['a dropped connection', rejectedWith(0, '')],
    ['a server error', rejectedWith(500, '')],
    ['a rule the form did not catch', rejectedWith(400, 'urn:drinkit:problem:product:name-length')],
  ])('reports %s as unreachable', (_case, error) => {
    create.mockReturnValue(throwError(() => error));

    store.submit('bar-alfa', aNewProduct);

    expect(store.status()).toBe('unreachable');
  });

  it('lets a new attempt through after a failure', () => {
    create.mockReturnValueOnce(throwError(() => rejectedWith(0, '')));
    create.mockReturnValueOnce(new Subject<Product>());

    store.submit('bar-alfa', aNewProduct);
    store.submit('bar-alfa', aNewProduct);

    expect(create).toHaveBeenCalledTimes(2);
    expect(store.isSending()).toBe(true);
  });
});
