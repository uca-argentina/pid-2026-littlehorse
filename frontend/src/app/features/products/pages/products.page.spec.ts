import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { fireEvent, render, screen, within } from '@testing-library/angular';
import { PRODUCTS_URL, PRODUCT_PLACEHOLDER } from '../products.service';
import type { Product } from '../products.service';
import { ProductsPage } from './products.page';

const theMenu: Product[] = [
  {
    id: 'id-1',
    name: 'Gin Tonic',
    description: 'Gin, tónica y una rodaja de lima.',
    imageUrl: 'https://images.example.com/gin-tonic.jpg',
    price: 4500,
    stock: 20,
    isAvailable: true,
    isSoldOut: false,
    isActive: true,
  },
  {
    id: 'id-2',
    name: 'Aperol Spritz',
    description: null,
    imageUrl: null,
    price: 5200.5,
    stock: 0,
    isAvailable: true,
    isSoldOut: true,
    isActive: true,
  },
  {
    id: 'id-3',
    name: 'Daiquiri',
    description: 'Ron, frutilla, limón',
    imageUrl: null,
    price: 4800,
    stock: 3,
    isAvailable: false,
    isSoldOut: false,
    isActive: false,
  },
];

async function openScreen() {
  const rendered = await render(ProductsPage, {
    inputs: { venueSlug: 'bar-alfa' },
    providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
  });

  return { rendered, http: TestBed.inject(HttpTestingController) };
}

async function openScreenShowing(products: Product[]) {
  const { rendered, http } = await openScreen();

  http.expectOne(PRODUCTS_URL).flush(products);
  await rendered.fixture.whenStable();

  return rendered;
}

describe('ProductsPage', () => {
  it('lists every product of the venue', async () => {
    await openScreenShowing(theMenu);

    expect(screen.getByText('Gin Tonic')).not.toBeNull();
    expect(screen.getByText('Aperol Spritz')).not.toBeNull();
    expect(screen.getByText('Daiquiri')).not.toBeNull();
  });

  // Pesos, with the thousands separator the venue reads. Cents only when
  // there are some: "$ 4.500" is how the price is written on the menu.
  it('shows the price in pesos, with cents only when there are some', async () => {
    await openScreenShowing(theMenu);

    expect(screen.getByText(/\$\s?4\.500$/)).not.toBeNull();
    expect(screen.getByText(/\$\s?5\.200,50/)).not.toBeNull();
  });

  it('shows how many are left of each product', async () => {
    await openScreenShowing(theMenu);

    expect(screen.getByText(/20 en stock/i)).not.toBeNull();
  });

  // Decided on 2026-09-14: stock at zero sells a product out on its own. The
  // administrator sees it here, before a customer tries to order it.
  it('marks a product with no stock left', async () => {
    await openScreenShowing(theMenu);

    const row = screen.getByRole('listitem', { name: /aperol spritz/i });

    // Once, and only once: beside the switch, where it also explains why the
    // switch will not move. The stock column says how many are left and the
    // chips are for the soft delete, so nothing else in the row repeats it.
    expect(within(row).getByText('Sin stock')).not.toBeNull();
  });

  // US-08 keeps deactivated products in the listing so old orders still point
  // somewhere. Showing them identical to the rest would be worse than hiding
  // them: the administrator would think customers can still see them.
  it('marks whatever was deactivated instead of hiding it', async () => {
    await openScreenShowing(theMenu);

    expect(screen.getByText(/dado de baja/i)).not.toBeNull();
  });

  it('shows the picture when there is one and the placeholder when there is not', async () => {
    await openScreenShowing(theMenu);

    expect(screen.getByRole('img', { name: 'Gin Tonic' }).getAttribute('src')).toBe(
      'https://images.example.com/gin-tonic.jpg',
    );
    expect(screen.getByRole('img', { name: 'Aperol Spritz' }).getAttribute('src')).toBe(
      PRODUCT_PLACEHOLDER,
    );
  });

  // US-06, criterion 5: a picture that cannot be fetched — storage down, blob
  // deleted — shows as the placeholder, never as a broken image.
  it('falls back to the placeholder when the picture cannot be loaded', async () => {
    const rendered = await openScreenShowing(theMenu);
    const picture = screen.getByRole('img', { name: 'Gin Tonic' });

    fireEvent.error(picture);
    await rendered.fixture.whenStable();

    expect(picture.getAttribute('src')).toBe(PRODUCT_PLACEHOLDER);
  });

  it('asks the API without naming a venue, because the token carries it', async () => {
    const { http } = await openScreen();

    expect(http.expectOne(PRODUCTS_URL).request.method).toBe('GET');
  });

  it('leads to the form that adds a product', async () => {
    await openScreenShowing(theMenu);

    expect(screen.getByRole('link', { name: /nuevo producto/i })).not.toBeNull();
  });

  // The venue's network is saturated at midnight. An empty screen with no
  // explanation is indistinguishable from a venue with no products.
  it('says so when the listing cannot be loaded', async () => {
    const { rendered, http } = await openScreen();

    http.expectOne(PRODUCTS_URL).flush('', { status: 500, statusText: 'Server Error' });
    await rendered.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toContain('No pudimos');
  });

  it('says the menu is empty instead of showing nothing', async () => {
    await openScreenShowing([]);

    expect(screen.getByRole('status').textContent).toContain('Todavía no');
  });

  // US-07: the nightly switch, separate from stock and from the soft delete.
  // US-07: the nightly switch of the wireframe, separate from stock and from
  // the soft delete. "Disponible esta noche".
  describe('the nightly switch', () => {
    const aGinTonic: Product = {
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

    function theSwitch(): HTMLElement {
      return screen.getByRole('switch', { name: /gin tonic/i });
    }

    it('is on for a drink the venue can serve tonight', async () => {
      await openScreenShowing([aGinTonic]);

      expect(theSwitch().getAttribute('aria-checked')).toBe('true');
      expect(screen.getByText('Disponible')).not.toBeNull();
    });

    it('lets the administrator turn a product off', async () => {
      const rendered = await openScreenShowing([aGinTonic]);

      theSwitch().click();

      TestBed.inject(HttpTestingController)
        .expectOne(`${PRODUCTS_URL}/id-1/mark-unavailable`)
        .flush({ ...aGinTonic, isAvailable: false });
      rendered.fixture.detectChanges();
      TestBed.inject(HttpTestingController)
        .expectOne(PRODUCTS_URL)
        .flush([{ ...aGinTonic, isAvailable: false }]);
      await rendered.fixture.whenStable();

      expect(theSwitch().getAttribute('aria-checked')).toBe('false');
      expect(screen.getByText('No disponible')).not.toBeNull();
    });

    it('lets the administrator turn a product back on', async () => {
      const anOffProduct: Product = { ...aGinTonic, isAvailable: false };
      const rendered = await openScreenShowing([anOffProduct]);

      theSwitch().click();

      TestBed.inject(HttpTestingController)
        .expectOne(`${PRODUCTS_URL}/id-1/mark-available`)
        .flush(aGinTonic);
      rendered.fixture.detectChanges();
      TestBed.inject(HttpTestingController).expectOne(PRODUCTS_URL).flush([aGinTonic]);
      await rendered.fixture.whenStable();

      expect(theSwitch().getAttribute('aria-checked')).toBe('true');
    });

    /**
     * Running out is not something the switch can undo, so the screen does not
     * offer to try: the drink comes back by being restocked, which is US-08.
     * The API refuses it too — this is the half that stops the tap happening.
     */
    it('is off and locked for a drink that ran out', async () => {
      const soldOut: Product = { ...aGinTonic, stock: 0, isSoldOut: true };
      await openScreenShowing([soldOut]);

      expect(theSwitch().getAttribute('aria-checked')).toBe('false');
      expect((theSwitch() as HTMLButtonElement).disabled).toBe(true);
      expect(screen.getByText('Sin stock')).not.toBeNull();
    });

    it('does not ask the API anything when the locked switch is tapped', async () => {
      const soldOut: Product = { ...aGinTonic, stock: 0, isSoldOut: true };
      await openScreenShowing([soldOut]);

      theSwitch().click();

      TestBed.inject(HttpTestingController).verify();
    });

    // Taken off the menu for good: the switch is about tonight, and there is no
    // tonight for something that is not on the menu any more.
    it('is off and locked for a drink that was taken off the menu', async () => {
      const gone: Product = { ...aGinTonic, isActive: false };
      await openScreenShowing([gone]);

      expect(theSwitch().getAttribute('aria-checked')).toBe('false');
      expect((theSwitch() as HTMLButtonElement).disabled).toBe(true);
    });

    // The venue's wifi drops mid-tap. Saying nothing leaves the administrator
    // sure they took a drink off sale while the bar keeps selling it.
    it('says so when the switch could not be saved', async () => {
      const rendered = await openScreenShowing([aGinTonic]);

      theSwitch().click();

      TestBed.inject(HttpTestingController)
        .expectOne(`${PRODUCTS_URL}/id-1/mark-unavailable`)
        .error(new ProgressEvent('error'), { status: 0, statusText: '' });
      rendered.fixture.detectChanges();
      await rendered.fixture.whenStable();

      expect(screen.getByRole('alert').textContent).toContain('No pudimos');
      // Nothing was saved, so the switch must not read as if it had been, and
      // it has to still be usable to try again.
      expect(theSwitch().getAttribute('aria-checked')).toBe('true');
      expect((theSwitch() as HTMLButtonElement).disabled).toBe(false);
    });

    it('clears the warning when the switch is tried again and works', async () => {
      const rendered = await openScreenShowing([aGinTonic]);

      theSwitch().click();
      TestBed.inject(HttpTestingController)
        .expectOne(`${PRODUCTS_URL}/id-1/mark-unavailable`)
        .error(new ProgressEvent('error'), { status: 0, statusText: '' });
      rendered.fixture.detectChanges();
      await rendered.fixture.whenStable();

      theSwitch().click();
      TestBed.inject(HttpTestingController)
        .expectOne(`${PRODUCTS_URL}/id-1/mark-unavailable`)
        .flush({ ...aGinTonic, isAvailable: false });
      rendered.fixture.detectChanges();
      TestBed.inject(HttpTestingController)
        .expectOne(PRODUCTS_URL)
        .flush([{ ...aGinTonic, isAvailable: false }]);
      await rendered.fixture.whenStable();

      expect(screen.queryByRole('alert')).toBeNull();
    });
  });

  describe('searching and filtering', () => {
    function search(term: string): void {
      fireEvent.input(screen.getByLabelText(/buscar/i), { target: { value: term } });
    }

    function pill(name: RegExp): HTMLButtonElement {
      return screen.getByRole('button', { name }) as HTMLButtonElement;
    }

    function listed(): string[] {
      return screen.getAllByRole('listitem').map((row) => row.getAttribute('aria-label') ?? '');
    }

    it('narrows the list to whatever matches what was typed', async () => {
      const rendered = await openScreenShowing(theMenu);

      search('aper');
      await rendered.fixture.whenStable();

      expect(listed()).toEqual(['Aperol Spritz']);
    });

    it('matches however it was capitalised', async () => {
      const rendered = await openScreenShowing(theMenu);

      search('GIN');
      await rendered.fixture.whenStable();

      expect(listed()).toEqual(['Gin Tonic']);
    });

    // Not the same situation as a venue with nothing loaded, and saying the
    // wrong one sends an administrator looking for a bug that is not there.
    it('says nothing matched, which is not an empty menu', async () => {
      const rendered = await openScreenShowing(theMenu);

      search('zzz');
      await rendered.fixture.whenStable();

      expect(screen.getByRole('status').textContent).toContain('Ningún producto coincide');
      expect(screen.queryByText(/todavía no hay ningún producto/i)).toBeNull();
    });

    // The two states an administrator goes looking for at night: what ran
    // out, and what was taken off the menu.
    it('counts everything, what is sold out and what was deactivated', async () => {
      await openScreenShowing(theMenu);

      expect(pill(/todos/i).textContent).toContain('3');
      expect(pill(/sin stock/i).textContent).toContain('1');
      expect(pill(/dados de baja/i).textContent).toContain('1');
    });

    it('shows only what is sold out when that filter is on', async () => {
      const rendered = await openScreenShowing(theMenu);

      pill(/sin stock/i).click();
      await rendered.fixture.whenStable();

      expect(listed()).toEqual(['Aperol Spritz']);
    });

    it('shows only what was deactivated when that filter is on', async () => {
      const rendered = await openScreenShowing(theMenu);

      pill(/dados de baja/i).click();
      await rendered.fixture.whenStable();

      expect(listed()).toEqual(['Daiquiri']);
    });

    it('narrows by filter and by what was typed at once', async () => {
      const rendered = await openScreenShowing(theMenu);

      pill(/sin stock/i).click();
      search('gin');
      await rendered.fixture.whenStable();

      expect(screen.queryAllByRole('listitem')).toHaveLength(0);
    });

    // Counted over what the search left, not over the whole menu: a pill that
    // promises one and then shows none reads as a filter that is broken.
    it('counts what the search left, not the whole menu', async () => {
      const rendered = await openScreenShowing(theMenu);

      search('gin');
      await rendered.fixture.whenStable();

      expect(pill(/todos/i).textContent).toContain('1');
      expect(pill(/sin stock/i).textContent).toContain('0');
    });
  });
});
