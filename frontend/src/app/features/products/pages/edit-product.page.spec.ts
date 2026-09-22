import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { fireEvent, render, screen } from '@testing-library/angular';
import { of, throwError } from 'rxjs';
import { ProblemTypes } from '../../../core/api/problem-types';
import { PRODUCTS_URL, PRODUCT_PLACEHOLDER, ProductsService } from '../products.service';
import type { Product } from '../products.service';
import { EditProductPage } from './edit-product.page';

const ginTonic: Product = {
  id: 'id-2',
  name: 'Gin Tonic',
  description: 'Gin, tónica y una rodaja de lima.',
  imageUrl: null,
  price: 4500,
  stock: 20,
  isAvailable: true,
  isSoldOut: false,
  isActive: true,
};

const theMenu: Product[] = [
  { ...ginTonic, id: 'id-1', name: 'Aperol Spritz', description: null, price: 5200 },
  ginTonic,
];

const rejectedWith = (status: number, type: string) =>
  vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status, error: { type } })));

async function openScreenFor(id: string, overrides: Record<string, unknown> = {}) {
  const products = {
    update: vi.fn().mockReturnValue(of({ ...ginTonic, name: 'Fernet con Coca', price: 3800 })),
    deactivate: vi.fn().mockReturnValue(of({ ...ginTonic, isActive: false })),
    uploadImage: vi.fn().mockReturnValue(of({ imageUrl: 'https://images.example.com/new.png' })),
    restock: vi.fn().mockReturnValue(of({ ...ginTonic, stock: 32 })),
    ...overrides,
  };

  const rendered = await render(EditProductPage, {
    inputs: { venueSlug: 'bar-alfa', id },
    providers: [
      provideRouter([{ path: ':venueSlug/staff/products', children: [] }]),
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: ProductsService, useValue: products },
    ],
  });

  TestBed.inject(HttpTestingController).expectOne(PRODUCTS_URL).flush(theMenu);
  await rendered.fixture.whenStable();

  return { rendered, products };
}

function field(label: RegExp): HTMLInputElement | HTMLTextAreaElement {
  return screen.getByLabelText(label) as HTMLInputElement | HTMLTextAreaElement;
}

function aFile(name: string, type: string, bytes = 4): File {
  return new File([new Uint8Array(bytes)], name, { type });
}

function choosePhoto(file: File): void {
  fireEvent.change(screen.getByLabelText(/^foto/i), { target: { files: [file] } });
}

function press(name: RegExp): void {
  screen.getByRole('button', { name }).click();
}

describe('EditProductPage', () => {
  it('names whoever is being corrected', async () => {
    await openScreenFor('id-2');

    expect(screen.getByRole('heading', { level: 1 }).textContent).toContain('Gin Tonic');
  });

  // US-08, criterion 1: what is already there is what gets corrected, not
  // typed again from scratch.
  it('starts with the current data already filled in', async () => {
    await openScreenFor('id-2');

    expect(field(/^nombre/i).value).toBe('Gin Tonic');
    expect(field(/descripción/i).value).toBe('Gin, tónica y una rodaja de lima.');
    expect(field(/precio/i).value).toBe('4500');
  });

  it('sends what was corrected', async () => {
    const { rendered, products } = await openScreenFor('id-2');

    fireEvent.input(field(/^nombre/i), { target: { value: 'Fernet con Coca' } });
    fireEvent.input(field(/precio/i), { target: { value: '3800' } });
    await rendered.fixture.whenStable();
    press(/guardar/i);

    expect(products['update']).toHaveBeenCalledWith('id-2', {
      name: 'Fernet con Coca',
      description: 'Gin, tónica y una rodaja de lima.',
      price: 3800,
    });
  });

  it('confirms the correction was saved', async () => {
    const { rendered } = await openScreenFor('id-2');

    press(/guardar/i);
    await rendered.fixture.whenStable();

    expect(screen.getByText(/guardad/i)).not.toBeNull();
  });

  // The form stops it, so the venue's connection is never part of finding out.
  it('does not save without a name', async () => {
    const { rendered, products } = await openScreenFor('id-2');

    fireEvent.input(field(/^nombre/i), { target: { value: '   ' } });
    await rendered.fixture.whenStable();
    press(/guardar/i);
    await rendered.fixture.whenStable();

    expect(products['update']).not.toHaveBeenCalled();
    expect(screen.getByText(/ponele un nombre/i)).not.toBeNull();
  });

  it('does not save a price of zero', async () => {
    const { rendered, products } = await openScreenFor('id-2');

    fireEvent.input(field(/precio/i), { target: { value: '0' } });
    await rendered.fixture.whenStable();
    press(/guardar/i);
    await rendered.fixture.whenStable();

    expect(products['update']).not.toHaveBeenCalled();
    expect(screen.getByText(/mayor a cero/i)).not.toBeNull();
  });

  it('says the name is already taken when the API refuses it', async () => {
    const { rendered } = await openScreenFor('id-2', {
      update: rejectedWith(409, ProblemTypes.productNameTaken),
    });

    press(/guardar/i);
    await rendered.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toContain('Ya hay un producto con ese nombre');
  });

  // US-08, criterion 3: taken off the menu, not deleted.
  it('takes it off the menu without offering to delete anything', async () => {
    const { products } = await openScreenFor('id-2');

    press(/dar de baja/i);

    expect(products['deactivate']).toHaveBeenCalledWith('id-2');
    expect(screen.queryByRole('button', { name: /borrar|eliminar/i })).toBeNull();
  });

  it('says so when nothing here has that id', async () => {
    const rendered = await render(EditProductPage, {
      inputs: { venueSlug: 'bar-alfa', id: 'nada' },
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ProductsService, useValue: {} },
      ],
    });

    TestBed.inject(HttpTestingController).expectOne(PRODUCTS_URL).flush(theMenu);
    await rendered.fixture.whenStable();

    expect(screen.getByRole('status').textContent).toContain('No encontramos');
  });

  describe('the photo', () => {
    it('shows the picture the product has now', async () => {
      await openScreenFor('id-2');

      expect(screen.getByRole('img', { name: /foto actual/i }).getAttribute('src')).toBe(
        PRODUCT_PLACEHOLDER,
      );
    });

    // The upload starts as soon as a file is chosen: the product already
    // exists, so there is no form to finish first.
    it('sends the file that was chosen', async () => {
      const { products } = await openScreenFor('id-2');
      const photo = aFile('gin-tonic.png', 'image/png');

      choosePhoto(photo);

      expect(products['uploadImage']).toHaveBeenCalledWith('id-2', photo);
    });

    it('shows the new picture and confirms it', async () => {
      const { rendered } = await openScreenFor('id-2');

      choosePhoto(aFile('gin-tonic.png', 'image/png'));
      await rendered.fixture.whenStable();

      expect(screen.getByRole('img', { name: /foto actual/i }).getAttribute('src')).toBe(
        'https://images.example.com/new.png',
      );
      expect(screen.getByText(/foto guardada/i)).not.toBeNull();
    });

    // The API would answer 415 to this, after the whole file crossed the
    // venue's connection. The screen says so first, and nothing is sent.
    it('does not send a file that is not a picture', async () => {
      const { rendered, products } = await openScreenFor('id-2');

      choosePhoto(aFile('menu.pdf', 'application/pdf'));
      await rendered.fixture.whenStable();

      expect(products['uploadImage']).not.toHaveBeenCalled();
      expect(screen.getByText(/JPEG, PNG o WebP/i)).not.toBeNull();
    });

    it('does not send a file over five megabytes', async () => {
      const { rendered, products } = await openScreenFor('id-2');

      choosePhoto(aFile('huge.jpg', 'image/jpeg', 5 * 1024 * 1024 + 1));
      await rendered.fixture.whenStable();

      expect(products['uploadImage']).not.toHaveBeenCalled();
      expect(screen.getByText(/5 MB/i)).not.toBeNull();
    });

    it('says so when the picture did not go up', async () => {
      const { rendered } = await openScreenFor('id-2', {
        uploadImage: rejectedWith(415, 'about:blank'),
      });

      choosePhoto(aFile('gin-tonic.png', 'image/png'));
      await rendered.fixture.whenStable();

      expect(screen.getByRole('alert').textContent).toContain('No pudimos subir la foto');
    });
  });

  describe('the stock', () => {
    it('says how many there are now', async () => {
      await openScreenFor('id-2');

      expect(screen.getByText(/hoy hay 20/i)).not.toBeNull();
    });

    // Units are added to what was left: the field is not "the new total".
    it('sends the units that arrived', async () => {
      const { rendered, products } = await openScreenFor('id-2');

      fireEvent.input(field(/unidades que llegaron/i), { target: { value: '12' } });
      await rendered.fixture.whenStable();
      press(/reponer/i);

      expect(products['restock']).toHaveBeenCalledWith('id-2', 12);
    });

    it('shows the new count once it was saved', async () => {
      const { rendered } = await openScreenFor('id-2');

      fireEvent.input(field(/unidades que llegaron/i), { target: { value: '12' } });
      await rendered.fixture.whenStable();
      press(/reponer/i);
      await rendered.fixture.whenStable();

      expect(screen.getByText(/hoy hay 32/i)).not.toBeNull();
      expect(screen.getByText(/stock repuesto/i)).not.toBeNull();
    });

    it('does not send zero, and says why', async () => {
      const { rendered, products } = await openScreenFor('id-2');

      fireEvent.input(field(/unidades que llegaron/i), { target: { value: '0' } });
      await rendered.fixture.whenStable();
      press(/reponer/i);
      await rendered.fixture.whenStable();

      expect(products['restock']).not.toHaveBeenCalled();
      expect(screen.getByText(/número entero mayor a cero/i)).not.toBeNull();
    });

    it('does not send half a bottle', async () => {
      const { rendered, products } = await openScreenFor('id-2');

      fireEvent.input(field(/unidades que llegaron/i), { target: { value: '2.5' } });
      await rendered.fixture.whenStable();
      press(/reponer/i);
      await rendered.fixture.whenStable();

      expect(products['restock']).not.toHaveBeenCalled();
    });

    it('says so when it could not be saved', async () => {
      const { rendered } = await openScreenFor('id-2', {
        restock: rejectedWith(500, 'about:blank'),
      });

      fireEvent.input(field(/unidades que llegaron/i), { target: { value: '12' } });
      await rendered.fixture.whenStable();
      press(/reponer/i);
      await rendered.fixture.whenStable();

      expect(screen.getByRole('alert').textContent).toContain('No pudimos reponer');
    });
  });

  it('offers the way back to the listing', async () => {
    await openScreenFor('id-2');

    expect(screen.getByRole('link', { name: /volver/i }).getAttribute('href')).toBe(
      '/bar-alfa/staff/products',
    );
  });
});
