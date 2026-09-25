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
  category: 'Drink',
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
  category: 'Drink',
};

const aPhoto = new File([new Uint8Array([0x89, 0x50, 0x4e, 0x47])], 'gin-tonic.png', {
  type: 'image/png',
});

describe('NewProductStore', () => {
  let store: NewProductStore;
  let create: ReturnType<typeof vi.fn>;
  let uploadImage: ReturnType<typeof vi.fn>;
  let router: Router;

  beforeEach(() => {
    create = vi.fn();
    uploadImage = vi.fn();

    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        NewProductStore,
        { provide: ProductsService, useValue: { create, uploadImage } },
      ],
    });

    store = TestBed.inject(NewProductStore);
    router = TestBed.inject(Router);
  });

  // Two taps on a slow connection must not create the same product twice.
  it('does not send twice while a request is in flight', () => {
    create.mockReturnValue(new Subject<Product>());

    store.submit('bar-alfa', aNewProduct, null);
    store.submit('bar-alfa', aNewProduct, null);

    expect(create).toHaveBeenCalledTimes(1);
  });

  it('goes back to the listing once the product exists', () => {
    create.mockReturnValue(of(created));
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    store.submit('bar-alfa', aNewProduct, null);

    expect(navigate).toHaveBeenCalledWith(['bar-alfa', 'staff', 'products']);
    expect(store.status()).toBe('idle');
  });

  it('reports a name that this venue already uses', () => {
    create.mockReturnValue(throwError(() => rejectedWith(409, ProblemTypes.productNameTaken)));

    store.submit('bar-alfa', aNewProduct, null);

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

    store.submit('bar-alfa', aNewProduct, null);

    expect(store.status()).toBe('unreachable');
  });

  // The picture needs the product's id to be filed under, so it goes second,
  // and the listing is not shown until it is there: arriving to a row with an
  // empty square where the photo should be reads as a failed upload.
  it('uploads the photo once the product exists, then goes to the listing', () => {
    create.mockReturnValue(of(created));
    uploadImage.mockReturnValue(of({ imageUrl: 'https://images.example.com/a.png' }));
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    store.submit('bar-alfa', aNewProduct, aPhoto);

    expect(uploadImage).toHaveBeenCalledWith(created.id, aPhoto);
    expect(navigate).toHaveBeenCalledWith(['bar-alfa', 'staff', 'products']);
  });

  it('does not upload anything when no photo was chosen', () => {
    create.mockReturnValue(of(created));
    vi.spyOn(router, 'navigate').mockResolvedValue(true);

    store.submit('bar-alfa', aNewProduct, null);

    expect(uploadImage).not.toHaveBeenCalled();
  });

  it('does not upload the photo when the product was refused', () => {
    create.mockReturnValue(throwError(() => rejectedWith(409, ProblemTypes.productNameTaken)));

    store.submit('bar-alfa', aNewProduct, aPhoto);

    expect(uploadImage).not.toHaveBeenCalled();
  });

  // The product is on the menu by then, so this is not "nothing was created":
  // it is a different message, and the screen must not offer to create it again.
  it('reports a photo that could not be uploaded as its own failure', () => {
    create.mockReturnValue(of(created));
    uploadImage.mockReturnValue(throwError(() => rejectedWith(0, '')));
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    store.submit('bar-alfa', aNewProduct, aPhoto);

    expect(store.status()).toBe('imageFailed');
    expect(navigate).not.toHaveBeenCalled();
  });

  it('lets a new attempt through after a failure', () => {
    create.mockReturnValueOnce(throwError(() => rejectedWith(0, '')));
    create.mockReturnValueOnce(new Subject<Product>());

    store.submit('bar-alfa', aNewProduct, null);
    store.submit('bar-alfa', aNewProduct, null);

    expect(create).toHaveBeenCalledTimes(2);
    expect(store.isSending()).toBe(true);
  });
});
