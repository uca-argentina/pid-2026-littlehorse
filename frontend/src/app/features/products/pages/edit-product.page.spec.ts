import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { fireEvent, render, screen } from '@testing-library/angular';
import { Subject, of, throwError } from 'rxjs';
import { ProblemTypes } from '../../../core/api/problem-types';
import { CATEGORIES_URL } from '../../categories/categories.service';
import { PRODUCTS_URL, ProductsService } from '../products.service';
import type { Product } from '../products.service';
import { EditProductPage } from './edit-product.page';

const noAudit = { createdAt: null, createdBy: null, lastModifiedAt: null, lastModifiedBy: null };

const ginTonic: Product = {
  id: 'id-2',
  name: 'Gin Tonic',
  description: 'Gin, tónica y una rodaja de lima.',
  imageUrl: 'https://images.example.com/gin-tonic.png',
  price: 4500,
  categoryId: 'category-drinks',
  isAvailable: true,
  isActive: true,

  audit: noAudit,
};

const fernet: Product = {
  ...ginTonic,
  audit: {
    createdAt: '2026-09-27T21:00:00Z',
    createdBy: 'euge.q',
    lastModifiedAt: '2026-09-28T01:30:00Z',
    lastModifiedBy: 'nico.r',
  },
  id: 'id-3',
  name: 'Fernet con Coca',
  imageUrl: null,
  isAvailable: false,
};

const theMenu: Product[] = [
  { ...ginTonic, id: 'id-1', name: 'Aperol Spritz', description: null, price: 5200 },
  ginTonic,
  fernet,
];

const rejectedWith = (status: number, type = '') =>
  vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status, error: { type } })));

async function openScreenFor(id: string, overrides: Record<string, unknown> = {}) {
  const products = {
    update: vi.fn().mockReturnValue(of(ginTonic)),
    uploadImage: vi.fn().mockReturnValue(of({ imageUrl: 'https://images.example.com/new.png' })),
    markAvailable: vi.fn().mockReturnValue(of(ginTonic)),
    markUnavailable: vi.fn().mockReturnValue(of({ ...ginTonic, isAvailable: false })),
    deactivate: vi.fn().mockReturnValue(of({ ...ginTonic, isActive: false })),
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

  const http = TestBed.inject(HttpTestingController);

  http.expectOne(PRODUCTS_URL).flush(theMenu);
  // The form only exists once the product was found, so its own request for the
  // categories is made after that answer, not alongside it — and never made at
  // all for an id nothing here has.
  if (theMenu.some((product) => product.id === id)) {
    const categoriesRequest = await vi.waitFor(() => http.expectOne(CATEGORIES_URL));

    categoriesRequest.flush([
      { id: 'category-drinks', name: 'Tragos' },
      { id: 'category-beer', name: 'Cervezas' },
    ]);
  }

  await rendered.fixture.whenStable();

  return { rendered, products };
}

// jsdom has no object URLs.
beforeEach(() => {
  vi.stubGlobal('URL', {
    ...URL,
    createObjectURL: vi.fn((file: File) => `blob:preview/${file.name}`),
    revokeObjectURL: vi.fn(),
  });
});

afterEach(() => vi.unstubAllGlobals());

function field(label: RegExp): HTMLInputElement {
  return screen.getByLabelText(label) as HTMLInputElement;
}

function type(label: RegExp, value: string): void {
  fireEvent.input(field(label), { target: { value } });
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

function category(name: RegExp): HTMLInputElement {
  return screen.getByRole('radio', { name }) as HTMLInputElement;
}

function nightlySwitch(): HTMLButtonElement {
  return screen.getByRole('switch', { name: /disponible/i }) as HTMLButtonElement;
}

describe('EditProductPage', () => {
  describe('while the product loads', () => {
    // Nothing answered yet: the listing request stays pending.
    async function openScreenLoading() {
      return render(EditProductPage, {
        inputs: { venueSlug: 'bar-alfa', id: 'id-2' },
        providers: [
          provideRouter([]),
          provideHttpClient(),
          provideHttpClientTesting(),
          { provide: ProductsService, useValue: {} },
        ],
      });
    }

    it('draws the outline of the form', async () => {
      await openScreenLoading();

      const skeleton = screen.getByTestId('product-skeleton');
      expect(skeleton.closest('[aria-busy="true"]')).not.toBeNull();
      expect(screen.getByRole('status').textContent).toContain('Buscando');
    });

    it('drops the outline once the product arrives', async () => {
      await openScreenFor('id-2');

      expect(screen.queryByTestId('product-skeleton')).toBeNull();
    });

    // A blank page with only "Volver al listado" says nothing about what
    // happened, or whether trying again could help.
    it('says so and offers to try again when the product cannot be fetched', async () => {
      const rendered = await openScreenLoading();
      const http = TestBed.inject(HttpTestingController);

      http.expectOne(PRODUCTS_URL).flush('', { status: 500, statusText: 'Server Error' });
      await rendered.fixture.whenStable();

      expect(screen.getByRole('alert').textContent).toContain('No pudimos traer este producto');
      screen.getByRole('button', { name: /reintentar/i }).click();
      rendered.fixture.detectChanges();

      // The last failure goes away while the new attempt is under way.
      expect(screen.queryByRole('alert')).toBeNull();
      http.expectOne(PRODUCTS_URL);
    });

    it('does not offer to retry when the account is no longer an administrator', async () => {
      const rendered = await openScreenLoading();

      TestBed.inject(HttpTestingController)
        .expectOne(PRODUCTS_URL)
        .flush(
          { type: ProblemTypes.forbidden },
          {
            status: 403,
            statusText: 'Forbidden',
            headers: { 'Content-Type': 'application/problem+json' },
          },
        );
      await rendered.fixture.whenStable();

      expect(screen.getByRole('alert').textContent).toContain('ya no lo es');
      expect(screen.queryByRole('button', { name: /reintentar/i })).toBeNull();
    });

    describe('when it takes long', () => {
      beforeEach(() => vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] }));
      afterEach(() => vi.useRealTimers());

      it('says it is still on it after a few seconds', async () => {
        const rendered = await openScreenLoading();

        expect(screen.getByRole('status').textContent).not.toContain('tardando');

        vi.advanceTimersByTime(5000);
        rendered.fixture.detectChanges();

        expect(screen.getByRole('status').textContent).toContain('tardando');
      });
    });
  });

  // US-30, criterion 2: who changed the price, and when.
  it('says who made the product and who last changed it', async () => {
    await openScreenFor('id-3');

    expect(screen.getByText(/creado por euge\.q/i)).not.toBeNull();
    expect(screen.getByText(/última modificación por nico\.r/i)).not.toBeNull();
  });

  it('says there is no record for a product from before the audit existed', async () => {
    await openScreenFor('id-2');

    expect(screen.getByText(/sin registro de quién lo creó ni de cuándo/i)).not.toBeNull();
  });

  // The same form as a new product, so what tells the two apart is the title
  // and the button.
  it('says it is correcting a product, not creating one', async () => {
    await openScreenFor('id-2');

    expect(screen.getByRole('heading', { level: 1 }).textContent).toMatch(/editar producto/i);
    expect(screen.getByRole('button', { name: /guardar cambios/i })).not.toBeNull();
    expect(screen.queryByRole('button', { name: /crear producto/i })).toBeNull();
  });

  // US-08, criterion 1: what is already there is what gets corrected, not
  // typed again from scratch.
  it('starts with the current data already filled in', async () => {
    await openScreenFor('id-2');

    expect(field(/^nombre/i).value).toBe('Gin Tonic');
    expect(field(/descripción/i).value).toBe('Gin, tónica y una rodaja de lima.');
    expect(field(/precio/i).value).toBe('4500');
    expect(nightlySwitch().getAttribute('aria-checked')).toBe('true');
  });

  // How many are left is the night's, and it is adjusted on the night's own
  // screen: here a total typed over the product would mean nothing.
  it('does not offer to touch the stock', async () => {
    await openScreenFor('id-2');

    expect(screen.queryByText(/en stock/i)).toBeNull();
    expect(screen.queryByRole('button', { name: /ajustar/i })).toBeNull();
  });

  it('sends the correction, and only that, then goes back to the listing', async () => {
    const { rendered, products } = await openScreenFor('id-2');

    type(/^nombre/i, '  Gin Tonic Doble  ');
    type(/precio/i, '5200');
    press(/guardar cambios/i);
    await rendered.fixture.whenStable();

    expect(products['update']).toHaveBeenCalledWith('id-2', {
      name: 'Gin Tonic Doble',
      description: 'Gin, tónica y una rodaja de lima.',
      price: 5200,
      categoryId: 'category-drinks',
    });
    expect(products['uploadImage']).not.toHaveBeenCalled();
    expect(products['markAvailable']).not.toHaveBeenCalled();
    expect(products['markUnavailable']).not.toHaveBeenCalled();
    expect(TestBed.inject(Router).url).toBe('/bar-alfa/staff/products');
  });

  // US-14: unlike the creation form, this screen starts from a product that already
  // has one, so the field opens on it instead of empty.
  it('opens with the category the product already has selected', async () => {
    await openScreenFor('id-2');

    expect(category(/tragos/i).checked).toBe(true);
  });

  it('sends the category that was changed to', async () => {
    const { rendered, products } = await openScreenFor('id-2');

    type(/^nombre/i, 'Gin Tonic Doble');
    type(/precio/i, '5200');
    fireEvent.click(category(/cervezas/i));
    press(/guardar cambios/i);
    await rendered.fixture.whenStable();

    expect(products['update']).toHaveBeenCalledWith(
      'id-2',
      expect.objectContaining({ categoryId: 'category-beer' }),
    );
  });

  it('shows the current photo until another one is chosen', async () => {
    const { rendered } = await openScreenFor('id-2');

    expect(screen.getByRole('img', { name: /foto actual/i }).getAttribute('src')).toBe(
      'https://images.example.com/gin-tonic.png',
    );

    choosePhoto(aFile('new.png', 'image/png'));
    await rendered.fixture.whenStable();

    expect(screen.getByRole('img', { name: /vista previa/i }).getAttribute('src')).toBe(
      'blob:preview/new.png',
    );
  });

  // Same as a new product: nothing goes up until the administrator saves.
  it('uploads the chosen photo when saving, not when choosing it', async () => {
    const { rendered, products } = await openScreenFor('id-2');
    const photo = aFile('new.png', 'image/png');

    choosePhoto(photo);
    await rendered.fixture.whenStable();
    expect(products['uploadImage']).not.toHaveBeenCalled();

    press(/guardar cambios/i);
    await rendered.fixture.whenStable();

    expect(products['uploadImage']).toHaveBeenCalledWith('id-2', photo);
  });

  it('goes back to the current photo when the chosen one is taken away', async () => {
    const { rendered, products } = await openScreenFor('id-2');

    choosePhoto(aFile('new.png', 'image/png'));
    await rendered.fixture.whenStable();
    press(/sacar la foto/i);
    await rendered.fixture.whenStable();
    press(/guardar cambios/i);
    await rendered.fixture.whenStable();

    expect(screen.queryByRole('img', { name: /vista previa/i })).toBeNull();
    expect(products['uploadImage']).not.toHaveBeenCalled();
  });

  it('turns it off for tonight when the switch is flipped', async () => {
    const { rendered, products } = await openScreenFor('id-2');

    nightlySwitch().click();
    await rendered.fixture.whenStable();
    press(/guardar cambios/i);
    await rendered.fixture.whenStable();

    expect(products['markUnavailable']).toHaveBeenCalledWith('id-2');
    expect(products['markAvailable']).not.toHaveBeenCalled();
  });

  it('says the name is already taken, and saves nothing else', async () => {
    const { rendered, products } = await openScreenFor('id-2', {
      update: rejectedWith(409, ProblemTypes.productNameTaken),
    });

    type(/precio/i, '5200');
    press(/guardar cambios/i);
    await rendered.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toContain('Ya hay un producto con ese nombre');
    expect(products['uploadImage']).not.toHaveBeenCalled();
    expect(products['markUnavailable']).not.toHaveBeenCalled();
  });

  it('says so when nothing could be saved', async () => {
    const { rendered } = await openScreenFor('id-2', { update: rejectedWith(0) });

    press(/guardar cambios/i);
    await rendered.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toContain('No pudimos guardarlo');
  });

  // The correction already went through when a later step failed, so saying
  // nothing was saved would be a lie.
  it('says part of it was saved when a later step fails', async () => {
    const { rendered } = await openScreenFor('id-2', { markUnavailable: rejectedWith(0) });

    nightlySwitch().click();
    await rendered.fixture.whenStable();
    press(/guardar cambios/i);
    await rendered.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toContain('una parte');
  });

  it('cannot be submitted twice while saving', async () => {
    const { rendered, products } = await openScreenFor('id-2', {
      update: vi.fn().mockReturnValue(new Subject<Product>()),
    });

    press(/guardar cambios/i);
    await rendered.fixture.whenStable();

    const button = screen.getByRole('button', { name: /guardando/i }) as HTMLButtonElement;
    button.click();

    expect(products['update']).toHaveBeenCalledTimes(1);
    expect(button.disabled).toBe(true);
  });

  // US-08, criterion 3: taken off the menu, not deleted.
  it('takes it off the menu once confirmed, without offering to delete anything', async () => {
    const { rendered, products } = await openScreenFor('id-2');

    press(/dar de baja/i);
    await rendered.fixture.whenStable();
    press(/confirmar la baja/i);

    expect(products['deactivate']).toHaveBeenCalledWith('id-2');
    expect(screen.queryByRole('button', { name: /borrar|eliminar/i })).toBeNull();
  });

  // Nothing on this screen brings it back, so one stray tap is not enough.
  it('does not take it off the menu on the first tap', async () => {
    const { rendered, products } = await openScreenFor('id-2');

    press(/dar de baja/i);
    await rendered.fixture.whenStable();

    expect(products['deactivate']).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: /confirmar la baja/i })).not.toBeNull();
  });

  it('backs out without taking it off the menu', async () => {
    const { rendered, products } = await openScreenFor('id-2');

    press(/dar de baja/i);
    await rendered.fixture.whenStable();
    press(/mejor no/i);
    await rendered.fixture.whenStable();

    expect(products['deactivate']).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: /dar de baja/i })).not.toBeNull();
  });

  // A double tap lands its second touch where the first one was: that spot
  // has to back out, not confirm.
  it('puts the way back first, where the first tap landed', async () => {
    const { rendered } = await openScreenFor('id-2');

    press(/dar de baja/i);
    await rendered.fixture.whenStable();

    const [first] = screen
      .getAllByRole('button')
      .filter((button) => /mejor no|confirmar la baja/i.test(button.textContent ?? ''));

    expect(first.textContent).toMatch(/mejor no/i);
  });

  it('says so when nothing here has that id', async () => {
    await openScreenFor('nada');

    expect(screen.getByRole('status').textContent).toContain('No encontramos');
  });

  it('offers a way back to the listing without saving', async () => {
    await openScreenFor('id-2');

    expect(screen.getByRole('link', { name: /cancelar/i }).getAttribute('href')).toBe(
      '/bar-alfa/staff/products',
    );
  });
});
