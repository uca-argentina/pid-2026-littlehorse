import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { fireEvent, render, screen } from '@testing-library/angular';
import { Subject, of, throwError } from 'rxjs';
import { ProblemTypes } from '../../../core/api/problem-types';
import { CATEGORIES_URL } from '../../categories/categories.service';
import type { Category } from '../../categories/categories.service';
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
  categoryId: 'category-drinks',
  isAvailable: true,
  isSoldOut: false,
  isActive: true,
};

const rejectedWith = (status: number, type: string) =>
  vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status, error: { type } })));

/** The venue's own categories, as the API lists them. */
const theCategories: Category[] = [
  { id: 'category-drinks', name: 'Tragos' },
  { id: 'category-beer', name: 'Cervezas' },
  { id: 'category-soft', name: 'Sin alcohol' },
];

async function openScreen(
  create = vi.fn().mockReturnValue(of(created)),
  uploadImage = vi.fn().mockReturnValue(of({ imageUrl: 'https://images.example.com/a.png' })),
  categories: Category[] | 'unreachable' | 'pending' = theCategories,
) {
  const rendered = await render(NewProductPage, {
    inputs: { venueSlug: 'bar-alfa' },
    providers: [
      // The screen navigates back to the listing for real: without a route to
      // land on, the router rejects and that masks the actual assertion.
      provideRouter([{ path: ':venueSlug/staff/products', children: [] }]),
      provideHttpClient(),
      provideHttpClientTesting(),
      NewProductStore,
      { provide: ProductsService, useValue: { create, uploadImage } },
    ],
  });

  const http = TestBed.inject(HttpTestingController);

  if (categories === 'unreachable') {
    http.expectOne(CATEGORIES_URL).flush('', { status: 500, statusText: 'Server Error' });
  } else if (categories !== 'pending') {
    http.expectOne(CATEGORIES_URL).flush(categories);
  }

  // Not stable while the request is in flight: waiting for it would never end.
  if (categories === 'pending') rendered.fixture.detectChanges();
  else await rendered.fixture.whenStable();

  return { rendered, create, uploadImage, http };
}

function aFile(name: string, type: string, bytes = 4): File {
  return new File([new Uint8Array(bytes)], name, { type });
}

function choosePhoto(file: File): void {
  fireEvent.change(screen.getByLabelText(/foto/i), { target: { files: [file] } });
}

// jsdom has no object URLs. The preview is whatever the browser hands back
// for the file, and this is the closest a test can get to checking that.
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

function category(name: RegExp): HTMLInputElement {
  return screen.getByRole('radio', { name }) as HTMLInputElement;
}

function fillAGinTonic(): void {
  type(/^nombre/i, 'Gin Tonic');
  type(/descripción/i, 'Gin, tónica y una rodaja de lima.');
  type(/precio/i, '4500');
  type(/stock/i, '20');
  fireEvent.click(category(/tragos/i));
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
    fireEvent.click(category(/tragos/i));
    save();

    expect(create).toHaveBeenCalledWith({
      name: 'Gin Tonic',
      description: 'Gin, tónica y una rodaja de lima.',
      imageUrl: null,
      categoryId: 'category-drinks',
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
    fireEvent.click(category(/tragos/i));
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
    fireEvent.click(category(/tragos/i));
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

  // US-06, criterion 4. The photo is optional, and when there is one it goes
  // up with the product: the administrator saves once.
  it('uploads the chosen photo along with the product', async () => {
    const { rendered, uploadImage } = await openScreen();
    const photo = aFile('gin-tonic.png', 'image/png');

    fillAGinTonic();
    choosePhoto(photo);
    save();
    await rendered.fixture.whenStable();

    expect(uploadImage).toHaveBeenCalledWith(created.id, photo);
  });

  // What the administrator wants to check is the picture, not the file name:
  // whether it is the right one, and whether it is upright.
  it('shows a preview of the photo that was chosen', async () => {
    const { rendered } = await openScreen();

    choosePhoto(aFile('gin-tonic.png', 'image/png'));
    await rendered.fixture.whenStable();

    expect(screen.getByRole('img', { name: /vista previa/i }).getAttribute('src')).toBe(
      'blob:preview/gin-tonic.png',
    );
  });

  // Object URLs hold the file in memory until they are released.
  it('releases the preview when the photo is taken away', async () => {
    const { rendered } = await openScreen();

    choosePhoto(aFile('gin-tonic.png', 'image/png'));
    await rendered.fixture.whenStable();
    screen.getByRole('button', { name: /sacar la foto/i }).click();
    await rendered.fixture.whenStable();

    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:preview/gin-tonic.png');
    expect(screen.queryByRole('img', { name: /vista previa/i })).toBeNull();
  });

  // The API would answer 415 and 413 to these, after the whole file went up
  // over the venue's connection. The form says so before sending a byte.
  it('refuses a file that is not a picture we show, and says so', async () => {
    const { rendered, create } = await openScreen();

    fillAGinTonic();
    choosePhoto(aFile('menu.pdf', 'application/pdf'));
    save();
    await rendered.fixture.whenStable();

    expect(create).not.toHaveBeenCalled();
    expect(screen.getByText(/JPEG, PNG o WebP/i)).not.toBeNull();
  });

  it('refuses a picture over five megabytes, and says so', async () => {
    const { rendered, create } = await openScreen();

    fillAGinTonic();
    choosePhoto(aFile('huge.jpg', 'image/jpeg', 5 * 1024 * 1024 + 1));
    save();
    await rendered.fixture.whenStable();

    expect(create).not.toHaveBeenCalled();
    expect(screen.getByText(/5 MB/)).not.toBeNull();
  });

  // The product exists by now: offering "Crear producto" again would end in
  // "that name is taken". What is left to do is to go and see it.
  it('says the product was created when only the photo failed, and leads to the listing', async () => {
    const { rendered } = await openScreen(
      undefined,
      vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status: 0 }))),
    );

    fillAGinTonic();
    choosePhoto(aFile('gin-tonic.png', 'image/png'));
    save();
    await rendered.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toContain('se creó');
    expect(screen.queryByRole('button', { name: /crear producto/i })).toBeNull();
    expect(screen.getByRole('link', { name: /ir al listado/i }).getAttribute('href')).toBe(
      '/bar-alfa/staff/products',
    );
  });

  it('offers a way back to the listing without saving', async () => {
    await openScreen();

    expect(screen.getByRole('link', { name: /cancelar/i }).getAttribute('href')).toBe(
      '/bar-alfa/staff/products',
    );
  });

  // Purely visual until US-07 gives it something to switch: on by default,
  // toggleable, and not part of what gets sent.
  it('shows the availability switch on by default, and it does not send it yet', async () => {
    const { rendered, create } = await openScreen();
    const toggle = screen.getByRole('switch', { name: /disponible/i });

    expect(toggle.getAttribute('aria-checked')).toBe('true');

    toggle.click();
    await rendered.fixture.whenStable();
    expect(toggle.getAttribute('aria-checked')).toBe('false');

    fillAGinTonic();
    save();

    expect(create).toHaveBeenCalledWith({
      name: 'Gin Tonic',
      description: 'Gin, tónica y una rodaja de lima.',
      imageUrl: null,
      categoryId: 'category-drinks',
      price: 4500,
      stock: 20,
    });
  });

  // US-14, criterion 1: loading a product asks for its category.
  it('offers the categories the venue has, as the API lists them', async () => {
    await openScreen();

    expect(category(/tragos/i)).not.toBeNull();
    expect(category(/cervezas/i)).not.toBeNull();
    expect(category(/sin alcohol/i)).not.toBeNull();
  });

  it('refuses to save without a category chosen, and says why', async () => {
    const { rendered, create } = await openScreen();

    type(/^nombre/i, 'Gin Tonic');
    type(/precio/i, '4500');
    type(/stock/i, '20');
    save();
    await rendered.fixture.whenStable();

    expect(create).not.toHaveBeenCalled();
    expect(screen.getByText(/elegí una categoría/i)).not.toBeNull();
  });

  // What the venue made itself shows up here without the screen knowing it.
  it('offers a category that only this venue has', async () => {
    await openScreen(undefined, undefined, [
      ...theCategories,
      { id: 'category-wine', name: 'Vinos' },
    ]);

    expect(category(/vinos/i)).not.toBeNull();
  });

  it('says the categories are loading instead of showing an empty choice', async () => {
    await openScreen(undefined, undefined, 'pending');

    expect(screen.getByText(/cargando las categorías/i)).not.toBeNull();
    expect(screen.queryByRole('radio')).toBeNull();
  });

  it('says so and offers to try again when the categories cannot be fetched', async () => {
    const { rendered, http } = await openScreen(undefined, undefined, 'unreachable');

    expect(screen.getByText(/no pudimos traer las categorías/i)).not.toBeNull();

    screen.getByRole('button', { name: /reintentar/i }).click();
    rendered.fixture.detectChanges();
    http.expectOne(CATEGORIES_URL).flush(theCategories);
    await rendered.fixture.whenStable();

    expect(category(/tragos/i)).not.toBeNull();
  });

  // Nothing to choose from is a dead end for the form, so it points at the
  // way out instead of leaving an empty fieldset.
  it('leads to creating the first category when the venue has none', async () => {
    await openScreen(undefined, undefined, []);

    expect(screen.getByRole('link', { name: /creá la primera/i }).getAttribute('href')).toBe(
      '/bar-alfa/staff/categories/new',
    );
  });

  it('sends the category that was picked', async () => {
    const { create } = await openScreen();

    type(/^nombre/i, 'Gin Tonic');
    type(/precio/i, '4500');
    type(/stock/i, '20');
    fireEvent.click(category(/cervezas/i));
    save();

    expect(create).toHaveBeenCalledWith(expect.objectContaining({ categoryId: 'category-beer' }));
  });
});
