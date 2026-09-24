import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { fireEvent, render, screen } from '@testing-library/angular';
import { Subject, of, throwError } from 'rxjs';
import { ProblemTypes } from '../../../core/api/problem-types';
import { PRODUCTS_URL, ProductsService } from '../products.service';
import type { Product } from '../products.service';
import { EditProductPage } from './edit-product.page';

const ginTonic: Product = {
  id: 'id-2',
  name: 'Gin Tonic',
  description: 'Gin, tónica y una rodaja de lima.',
  imageUrl: 'https://images.example.com/gin-tonic.png',
  price: 4500,
  stock: 20,
  isAvailable: true,
  isSoldOut: false,
  isActive: true,
};

const fernet: Product = {
  ...ginTonic,
  id: 'id-3',
  name: 'Fernet con Coca',
  imageUrl: null,
  stock: 0,
  isSoldOut: true,
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
    adjustStock: vi.fn().mockReturnValue(of({ ...ginTonic, stock: 32 })),
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

  TestBed.inject(HttpTestingController).expectOne(PRODUCTS_URL).flush(theMenu);
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

interface Rendered {
  fixture: { whenStable: () => Promise<unknown> };
}

/** The stock is shown, not typed over: "Ajustar" opens it, on units that arrived. */
async function adjustBy(rendered: Rendered, units: string): Promise<void> {
  press(/^ajustar$/i);
  await rendered.fixture.whenStable();
  type(/unidades que llegaron/i, units);
  await rendered.fixture.whenStable();
}

/** The other way in: the total was loaded wrong, and this is the real one. */
async function correctTo(rendered: Rendered, total: string): Promise<void> {
  press(/^ajustar$/i);
  await rendered.fixture.whenStable();
  press(/corregir el total/i);
  await rendered.fixture.whenStable();
  type(/stock real/i, total);
  await rendered.fixture.whenStable();
}

function nightlySwitch(): HTMLButtonElement {
  return screen.getByRole('switch', { name: /disponible/i }) as HTMLButtonElement;
}

describe('EditProductPage', () => {
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

  // The stock is something to read here, not a field that invites typing a
  // new total over the sales made tonight.
  it('shows the current stock instead of a field to type it over', async () => {
    await openScreenFor('id-2');

    expect(screen.getByText(/20 en stock/i)).not.toBeNull();
    expect(screen.queryByLabelText(/unidades que llegaron/i)).toBeNull();
  });

  // Units are added, not set: the total they end up at has to be in plain
  // sight before saving.
  it('shows the total the stock ends up at once units are added', async () => {
    const { rendered } = await openScreenFor('id-2');

    await adjustBy(rendered, '12');

    expect(screen.getByText(/hoy hay 20/i)).not.toBeNull();
    expect(screen.getByText(/quedan 32 en total/i)).not.toBeNull();
  });

  // Unlike the rest of the form, which waits for the first save: a wrong
  // number of units next to "Las que pongas se suman" reads as accepted.
  it('says the units are wrong as soon as they are typed', async () => {
    const { rendered } = await openScreenFor('id-2');

    await adjustBy(rendered, '-200');

    expect(screen.getByText(/entero mayor a cero/i)).not.toBeNull();
    expect(screen.queryByText(/se suman/i)).toBeNull();
    expect(field(/unidades que llegaron/i).getAttribute('aria-invalid')).toBe('true');
  });

  // Loaded 20 when it was 5. What is sent is the difference, so a sale made
  // while the screen was open is not put back.
  it('corrects a stock that was loaded wrong by sending the difference', async () => {
    const { rendered, products } = await openScreenFor('id-2');

    await correctTo(rendered, '5');

    expect(screen.getByText(/pasa a 5 \(−15\)/i)).not.toBeNull();

    press(/guardar cambios/i);
    await rendered.fixture.whenStable();

    expect(products['adjustStock']).toHaveBeenCalledWith('id-2', -15);
  });

  it('says a corrected total has to be zero or more', async () => {
    const { rendered } = await openScreenFor('id-2');

    await correctTo(rendered, '-1');

    expect(screen.getByText(/cero o más/i)).not.toBeNull();
  });

  // Correcting to what is already there changes nothing, so nothing is sent.
  it('sends no adjustment when the corrected total is the same', async () => {
    const { rendered, products } = await openScreenFor('id-2');

    await correctTo(rendered, '20');
    press(/guardar cambios/i);
    await rendered.fixture.whenStable();

    expect(products['adjustStock']).not.toHaveBeenCalled();
  });

  it('backs out of adjusting without changing anything', async () => {
    const { rendered, products } = await openScreenFor('id-2');

    await adjustBy(rendered, '12');
    press(/no ajustar/i);
    await rendered.fixture.whenStable();
    press(/guardar cambios/i);
    await rendered.fixture.whenStable();

    expect(screen.getByText(/20 en stock/i)).not.toBeNull();
    expect(products['adjustStock']).not.toHaveBeenCalled();
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
    });
    expect(products['uploadImage']).not.toHaveBeenCalled();
    expect(products['adjustStock']).not.toHaveBeenCalled();
    expect(products['markAvailable']).not.toHaveBeenCalled();
    expect(products['markUnavailable']).not.toHaveBeenCalled();
    expect(TestBed.inject(Router).url).toBe('/bar-alfa/staff/products');
  });

  it('adds the units that arrived', async () => {
    const { rendered, products } = await openScreenFor('id-2');

    await adjustBy(rendered, '12');
    press(/guardar cambios/i);
    await rendered.fixture.whenStable();

    expect(products['adjustStock']).toHaveBeenCalledWith('id-2', 12);
  });

  it.each([
    ['zero', '0'],
    ['a negative number', '-1'],
    ['a number that is not whole', '1.5'],
  ])('refuses to add %s units, and says so', async (_case, units) => {
    const { rendered, products } = await openScreenFor('id-2');

    await adjustBy(rendered, units);
    press(/guardar cambios/i);
    await rendered.fixture.whenStable();

    expect(products['update']).not.toHaveBeenCalled();
    expect(screen.getByText(/entero mayor a cero/i)).not.toBeNull();
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

  // Running out locks the switch, same as in the listing. Adding units is the
  // way out, so typing them is what unlocks it.
  it('locks the switch of a product that ran out until units are added', async () => {
    const { rendered } = await openScreenFor('id-3');

    expect(nightlySwitch().disabled).toBe(true);

    await adjustBy(rendered, '5');
    await rendered.fixture.whenStable();

    expect(nightlySwitch().disabled).toBe(false);
  });

  // The domain refuses to turn on a product with nothing to sell, so the
  // stock has to arrive first.
  it('adds the stock before turning a product that ran out back on', async () => {
    const { rendered, products } = await openScreenFor('id-3');

    await adjustBy(rendered, '5');
    await rendered.fixture.whenStable();
    nightlySwitch().click();
    await rendered.fixture.whenStable();
    press(/guardar cambios/i);
    await rendered.fixture.whenStable();

    expect(products['adjustStock']).toHaveBeenCalledWith('id-3', 5);
    expect(products['markAvailable']).toHaveBeenCalledWith('id-3');
    expect(products['adjustStock'].mock.invocationCallOrder[0]).toBeLessThan(
      products['markAvailable'].mock.invocationCallOrder[0],
    );
  });

  it('says the name is already taken, and saves nothing else', async () => {
    const { rendered, products } = await openScreenFor('id-2', {
      update: rejectedWith(409, ProblemTypes.productNameTaken),
    });

    await adjustBy(rendered, '12');
    press(/guardar cambios/i);
    await rendered.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toContain('Ya hay un producto con ese nombre');
    expect(products['adjustStock']).not.toHaveBeenCalled();
    expect(field(/unidades que llegaron/i).value).toBe('12');
  });

  it('says so when nothing could be saved', async () => {
    const { rendered } = await openScreenFor('id-2', { update: rejectedWith(0) });

    press(/guardar cambios/i);
    await rendered.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toContain('No pudimos guardarlo');
  });

  // Units are added, so sending them again after a failure further down would
  // add them twice. Once they are in, the field closes over the new stock.
  it('does not add the same units twice when a later step fails', async () => {
    const { rendered } = await openScreenFor('id-2', { markUnavailable: rejectedWith(0) });

    await adjustBy(rendered, '12');
    nightlySwitch().click();
    await rendered.fixture.whenStable();
    press(/guardar cambios/i);
    await rendered.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toContain('una parte');
    expect(screen.queryByLabelText(/unidades que llegaron/i)).toBeNull();
    expect(screen.getByText(/32 en stock/i)).not.toBeNull();
  });

  // The screen showed 20 and the bar sold 18 while it was open: a correction
  // to 0 would take away 20, and only 2 are left. Nothing is written, and the
  // message says what there is now.
  it('says sales made meanwhile left less than the correction takes away', async () => {
    const { rendered } = await openScreenFor('id-2', {
      update: vi.fn().mockReturnValue(of({ ...ginTonic, stock: 2 })),
      adjustStock: rejectedWith(409, ProblemTypes.productStockMoved),
    });

    await correctTo(rendered, '0');
    press(/guardar cambios/i);
    await rendered.fixture.whenStable();

    const alert = screen.getByRole('alert').textContent ?? '';
    expect(alert).toContain('Se vendió mientras tanto');
    expect(alert).toContain('ahora hay 2');
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
  it('takes it off the menu without offering to delete anything', async () => {
    const { products } = await openScreenFor('id-2');

    press(/dar de baja/i);

    expect(products['deactivate']).toHaveBeenCalledWith('id-2');
    expect(screen.queryByRole('button', { name: /borrar|eliminar/i })).toBeNull();
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
