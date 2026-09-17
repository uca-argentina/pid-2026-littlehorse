import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { CART_STORAGE_PREFIX, Cart } from '../../../core/cart/cart';
import { BrowserStore } from '../../../core/storage/browser-store';
import { StoreInMemory } from '../../../core/storage/store-in-memory';
import { fireEvent, render, screen } from '@testing-library/angular';
import { ProblemTypes } from '../../../core/api/problem-types';
import { menuUrl } from '../menu.service';
import type { Menu } from '../menu.service';
import { MenuPage } from './menu.page';

const carta: Menu = {
  venueName: 'Bar Alfa',
  items: [
    {
      id: 'id-1',
      name: 'Gin Tonic',
      description: 'Gin, tónica, lima',
      imageUrl: 'https://images.example.com/gin.png',
      price: 4500,
      isOrderable: true,
    },
    {
      id: 'id-2',
      name: 'Fernet con Coca',
      description: 'Medida doble',
      imageUrl: null,
      price: 4000,
      isOrderable: true,
    },
    {
      id: 'id-3',
      name: 'Aperol Spritz',
      description: null,
      imageUrl: null,
      price: 6000,
      isOrderable: false,
    },
  ],
};

async function openScreen() {
  const rendered = await render(MenuPage, {
    inputs: { venueSlug: 'bar-alfa' },
    providers: [
      provideRouter([]),
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: BrowserStore, useValue: store },
    ],
  });

  return { rendered, http: TestBed.inject(HttpTestingController) };
}

async function openScreenShowing(menu: Menu) {
  const { rendered, http } = await openScreen();

  http.expectOne(menuUrl('bar-alfa')).flush(menu);
  await rendered.fixture.whenStable();

  return rendered;
}

function names(): string[] {
  return screen.getAllByRole('listitem').map((card) => card.querySelector('h2')?.textContent ?? '');
}

// A fresh StoreInMemory per test keeps an order built in one case out of the
// next.
let store: StoreInMemory;

describe('MenuPage', () => {
  beforeEach(() => {
    store = new StoreInMemory();
  });

  // Criterion 1: the venue of the QR, and not a venue picked from a screen.
  it('asks for the menu of the venue in the address', async () => {
    const { http } = await openScreen();

    expect(http.expectOne(menuUrl('bar-alfa')).request.method).toBe('GET');
  });

  it('shows what the venue sells', async () => {
    await openScreenShowing(carta);

    expect(names()).toEqual(['Gin Tonic', 'Fernet con Coca', 'Aperol Spritz']);
  });

  // The customer scanned a QR and never typed where they are, so the screen is
  // what tells them which bar this is.
  it('names the venue it belongs to', async () => {
    await openScreenShowing(carta);

    expect(screen.getByRole('heading', { level: 1 }).textContent).toContain('Bar Alfa');
  });

  it('writes the price the way it is read here', async () => {
    await openScreenShowing(carta);

    expect(screen.getByText('$ 4.500,00')).not.toBeNull();
  });

  // Criterion 2: nothing on this path asks for an account or an install.
  it('never asks anybody to sign in or install anything', async () => {
    await openScreenShowing(carta);

    expect(screen.queryByText(/iniciar sesión|entrar|instalar|crear (una )?cuenta/i)).toBeNull();
    expect(screen.queryByLabelText(/contraseña/i)).toBeNull();
  });

  // Criterion 3.
  it('says the menu is loading instead of showing a blank screen', async () => {
    await openScreen();

    expect(screen.getByRole('status').textContent).toContain('Buscando');
  });

  // Criterion 4. A dropped connection is the case that a retry can fix.
  it('says so and offers to try again when the menu cannot be fetched', async () => {
    const { rendered, http } = await openScreen();

    http.expectOne(menuUrl('bar-alfa')).flush('', { status: 500, statusText: 'Server Error' });
    await rendered.fixture.whenStable();

    expect(screen.getByRole('alert')).not.toBeNull();
    expect(screen.getByRole('button', { name: /reintentar/i })).not.toBeNull();
  });

  // detectChanges and not whenStable between the click and the expectation:
  // stability never arrives while a request is in flight, which is exactly the
  // state this test needs to catch the second one being made.
  it('asks again when somebody retries', async () => {
    const { rendered, http } = await openScreen();

    http.expectOne(menuUrl('bar-alfa')).flush('', { status: 500, statusText: 'Server Error' });
    await rendered.fixture.whenStable();

    screen.getByRole('button', { name: /reintentar/i }).click();
    rendered.fixture.detectChanges();

    http.expectOne(menuUrl('bar-alfa')).flush(carta);
    await rendered.fixture.whenStable();

    expect(names()).toContain('Gin Tonic');
  });

  /**
   * A slug no venue answers to. Offering "check your signal" and a Reintentar
   * that can never succeed sends somebody to fight their connection over a QR
   * that was never ours.
   */
  it('says the address belongs to no venue, and does not offer to retry', async () => {
    const { rendered, http } = await openScreen();

    http
      .expectOne(menuUrl('bar-alfa'))
      .flush({ type: ProblemTypes.venueNotFound }, { status: 404, statusText: 'Not Found' });
    await rendered.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toContain('no es de ningún boliche');
    expect(screen.queryByRole('button', { name: /reintentar/i })).toBeNull();
  });

  // Criterion 5: an empty list with no explanation reads as a broken app.
  it('says the venue has not loaded anything yet', async () => {
    await openScreenShowing({ venueName: 'Bar Alfa', items: [] });

    expect(screen.getByRole('status').textContent).toContain('todavía no cargó');
  });

  // A rule of the story: sold out has to be obvious at a glance, and it stays
  // on the menu rather than disappearing.
  it('marks what cannot be ordered instead of hiding it', async () => {
    await openScreenShowing(carta);

    expect(names()).toContain('Aperol Spritz');
    expect(screen.getByText(/sin stock/i)).not.toBeNull();
  });

  /**
   * Every card shows something where the picture goes, so a product with no
   * image never leaves a hole in the list. Queried by selector and not by role:
   * the photo carries an empty alt because the name is right beside it, which
   * makes it decorative and takes it out of the accessibility tree on purpose.
   */
  it('shows a placeholder for a product with no picture', async () => {
    const rendered = await openScreenShowing(carta);

    const images = [...rendered.container.querySelectorAll('img')] as HTMLImageElement[];

    expect(images).toHaveLength(carta.items.length);
    expect(images.some((image) => image.src.includes('product-placeholder'))).toBe(true);
  });

  describe('searching', () => {
    function search(term: string): void {
      fireEvent.input(screen.getByLabelText(/buscar/i, { selector: 'input' }), {
        target: { value: term },
      });
    }

    it('narrows the menu to what matches', async () => {
      const rendered = await openScreenShowing(carta);

      search('fer');
      await rendered.fixture.whenStable();

      expect(names()).toEqual(['Fernet con Coca']);
    });

    it('matches however it was capitalised', async () => {
      const rendered = await openScreenShowing(carta);

      search('GIN');
      await rendered.fixture.whenStable();

      expect(names()).toEqual(['Gin Tonic']);
    });

    it('says nothing matched, which is not an empty menu', async () => {
      const rendered = await openScreenShowing(carta);

      search('zzz');
      await rendered.fixture.whenStable();

      expect(screen.getByRole('status').textContent).toContain('No encontramos');
      expect(screen.queryByText(/todavía no cargó/i)).toBeNull();
    });
  });

  describe('adding to the order', () => {
    function plus(name: string): HTMLButtonElement {
      return screen.getByRole('button', {
        name: new RegExp(`agregar ${name}`, 'i'),
      }) as HTMLButtonElement;
    }

    function summary(): HTMLElement | null {
      return screen.queryByTestId('order-summary');
    }

    // US-10, criterion 1.
    it('puts what was tapped into the order', async () => {
      const rendered = await openScreenShowing(carta);

      plus('Gin Tonic').click();
      await rendered.fixture.whenStable();

      expect(TestBed.inject(Cart).count()).toBe(1);
    });

    // The order screen has no menu request of its own to get a photo from:
    // whatever the card was showing has to travel with the line.
    it('carries the picture the card was showing onto the line', async () => {
      const rendered = await openScreenShowing(carta);

      plus('Gin Tonic').click();
      await rendered.fixture.whenStable();

      expect(TestBed.inject(Cart).lines()[0].imageUrl).toBe('https://images.example.com/gin.png');
    });

    it('raises the count on a second tap instead of opening a second line', async () => {
      const rendered = await openScreenShowing(carta);

      plus('Gin Tonic').click();
      plus('Gin Tonic').click();
      await rendered.fixture.whenStable();

      expect(TestBed.inject(Cart).count()).toBe(2);
      expect(TestBed.inject(Cart).lines()).toHaveLength(1);
    });

    // Nothing that cannot be served can be ordered, which is the other half of
    // showing it: it stays on the menu, and the way to add it is gone.
    it('offers no way to add what ran out', async () => {
      await openScreenShowing(carta);

      expect(screen.queryByRole('button', { name: /agregar Aperol/i })).toBeNull();
    });

    // US-10, criterion 5: with nothing in the order there is nothing to go on to.
    it('shows no order summary until something is added', async () => {
      await openScreenShowing(carta);

      expect(summary()).toBeNull();
    });

    it('shows how many and how much once something is added', async () => {
      const rendered = await openScreenShowing(carta);

      plus('Gin Tonic').click();
      plus('Fernet con Coca').click();
      await rendered.fixture.whenStable();

      expect(summary()?.textContent).toContain('2 ítems');
      expect(summary()?.textContent).toContain('$ 8.500,00');
    });

    it('says one item in the singular', async () => {
      const rendered = await openScreenShowing(carta);

      plus('Gin Tonic').click();
      await rendered.fixture.whenStable();

      expect(summary()?.textContent).toContain('1 ítem');
      expect(summary()?.textContent).not.toContain('1 ítems');
    });

    // The strip is the way on to the order now that there is an order screen
    // to reach: it is the only thing at the bottom of a menu somebody is done
    // reading, so it has to be the thing that takes them forward.
    it('leads to the order of this venue', async () => {
      const rendered = await openScreenShowing(carta);

      plus('Gin Tonic').click();
      await rendered.fixture.whenStable();

      expect(summary()?.textContent).toContain('Ver pedido');
      expect(summary()?.getAttribute('href')).toBe('/bar-alfa/order');
    });

    /**
     * US-10, criterion 4: they took a phone call and came back. That the order
     * survives is the Cart's own test; what this one owns is that the menu
     * shows an order it did not watch being built.
     */
    it('shows an order that was already there when it opened', async () => {
      store.entries.set(
        `${CART_STORAGE_PREFIX}bar-alfa`,
        JSON.stringify([
          { productId: 'id-1', name: 'Gin Tonic', unitPrice: 4500, quantity: 2, note: null },
        ]),
      );

      await openScreenShowing(carta);

      expect(summary()?.textContent).toContain('2 ítems');
      expect(summary()?.textContent).toContain('$ 9.000,00');
    });

    describe('the note on a card', () => {
      // Matches either wording: "Escribir..." before there is a note,
      // "Editar..." once there is one.
      function noteButton(name: string): HTMLButtonElement {
        return screen.getByRole('button', {
          name: new RegExp(`(escribir una nota para|editar la nota de) ${name}`, 'i'),
        }) as HTMLButtonElement;
      }

      // US-10, criterion 5. Behind a button and not always open: a field under
      // every card turns a menu somebody is reading into a form to fill in.
      it('is written from the card itself', async () => {
        const rendered = await openScreenShowing(carta);

        plus('Gin Tonic').click();
        await rendered.fixture.whenStable();

        noteButton('Gin Tonic').click();
        await rendered.fixture.whenStable();

        fireEvent.input(screen.getByLabelText('Nota para Gin Tonic'), {
          target: { value: 'sin hielo' },
        });

        expect(TestBed.inject(Cart).noteOf('id-1')).toBe('sin hielo');
      });

      // Nothing to write a note on until the drink is in the order: the note
      // travels with the line, and there is no line yet.
      it('cannot be written on a drink that was not added', async () => {
        await openScreenShowing(carta);

        expect(
          screen.queryByRole('button', { name: /escribir una nota para Gin Tonic/i }),
        ).toBeNull();
      });

      it('shows on the card once it is written', async () => {
        const rendered = await openScreenShowing(carta);

        plus('Gin Tonic').click();
        await rendered.fixture.whenStable();

        noteButton('Gin Tonic').click();
        await rendered.fixture.whenStable();

        fireEvent.input(screen.getByLabelText('Nota para Gin Tonic'), {
          target: { value: 'sin hielo' },
        });
        screen.getByRole('button', { name: /listo con la nota de Gin Tonic/i }).click();
        await rendered.fixture.whenStable();

        expect(screen.getByTestId('note-Gin Tonic').textContent).toContain('sin hielo');
      });

      // Once there is something to edit, the button says so instead of
      // offering to add a note that already exists.
      it('offers to edit rather than add once a note exists', async () => {
        store.entries.set(
          `${CART_STORAGE_PREFIX}bar-alfa`,
          JSON.stringify([
            {
              productId: 'id-1',
              name: 'Gin Tonic',
              unitPrice: 4500,
              quantity: 1,
              note: 'sin hielo',
            },
          ]),
        );

        await openScreenShowing(carta);

        expect(screen.getByRole('button', { name: /editar la nota de Gin Tonic/i })).not.toBeNull();
        expect(screen.queryByRole('button', { name: /escribir una nota/i })).toBeNull();
      });

      it('opens with what was already written in it', async () => {
        store.entries.set(
          `${CART_STORAGE_PREFIX}bar-alfa`,
          JSON.stringify([
            {
              productId: 'id-1',
              name: 'Gin Tonic',
              unitPrice: 4500,
              quantity: 1,
              note: 'sin hielo',
            },
          ]),
        );

        const rendered = await openScreenShowing(carta);

        noteButton('Gin Tonic').click();
        await rendered.fixture.whenStable();

        expect(screen.getByLabelText<HTMLInputElement>('Nota para Gin Tonic').value).toBe(
          'sin hielo',
        );
      });
    });

    function minus(name: string): HTMLButtonElement {
      return screen.getByRole('button', {
        name: new RegExp(`quitar un ${name}`, 'i'),
      }) as HTMLButtonElement;
    }

    function counter(name: string): HTMLElement | null {
      return screen.queryByTestId(`quantity-${name}`);
    }

    // Nothing but the plus until it is in the order: a minus and a zero on
    // every card is two controls that do nothing on most of the menu.
    it('shows only the plus on a drink that is not in the order', async () => {
      await openScreenShowing(carta);

      expect(screen.queryByRole('button', { name: /quitar un Gin Tonic/i })).toBeNull();
      expect(counter('Gin Tonic')).toBeNull();
    });

    it('shows how many are in the order once one is added', async () => {
      const rendered = await openScreenShowing(carta);

      plus('Gin Tonic').click();
      await rendered.fixture.whenStable();

      expect(counter('Gin Tonic')?.textContent?.trim()).toBe('1');
      expect(minus('Gin Tonic')).not.toBeNull();
    });

    // US-10, criterion 2: the total follows at once.
    it('lowers the count and the total when one is taken out', async () => {
      const rendered = await openScreenShowing(carta);

      plus('Gin Tonic').click();
      plus('Gin Tonic').click();
      await rendered.fixture.whenStable();

      minus('Gin Tonic').click();
      await rendered.fixture.whenStable();

      expect(counter('Gin Tonic')?.textContent?.trim()).toBe('1');
      expect(summary()?.textContent).toContain('$ 4.500,00');
    });

    it('goes back to just the plus when the last one is taken out', async () => {
      const rendered = await openScreenShowing(carta);

      plus('Gin Tonic').click();
      await rendered.fixture.whenStable();

      minus('Gin Tonic').click();
      await rendered.fixture.whenStable();

      expect(counter('Gin Tonic')).toBeNull();
      expect(plus('Gin Tonic')).not.toBeNull();
      expect(summary()).toBeNull();
    });

    // Each card counts its own drink and nothing else.
    it('counts each drink on its own card', async () => {
      const rendered = await openScreenShowing(carta);

      plus('Gin Tonic').click();
      plus('Gin Tonic').click();
      plus('Fernet con Coca').click();
      await rendered.fixture.whenStable();

      expect(counter('Gin Tonic')?.textContent?.trim()).toBe('2');
      expect(counter('Fernet con Coca')?.textContent?.trim()).toBe('1');
    });
  });
});
