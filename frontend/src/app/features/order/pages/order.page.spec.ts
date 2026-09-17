import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { fireEvent, render, screen } from '@testing-library/angular';
import { Cart } from '../../../core/cart/cart';
import { BrowserStore } from '../../../core/storage/browser-store';
import { StoreInMemory } from '../../../core/storage/store-in-memory';
import { OrderPage } from './order.page';

const ginTonic = { id: 'id-1', name: 'Gin Tonic', price: 4500 };
const fernet = { id: 'id-2', name: 'Fernet con Coca', price: 4000 };

let store: StoreInMemory;

/** Nothing added: the screen as somebody reaches it with an empty order. */
const nothing = (): void => {
  // The order is whatever the store held, which in this case is nothing.
};

/** The screen as somebody reaches it: with whatever they already put together. */
async function openScreenWith(fill: (cart: Cart) => void = nothing) {
  const rendered = await render(OrderPage, {
    inputs: { venueSlug: 'bar-alfa' },
    providers: [provideRouter([]), { provide: BrowserStore, useValue: store }],
  });

  const cart = TestBed.inject(Cart);
  cart.open('bar-alfa');
  fill(cart);
  await rendered.fixture.whenStable();

  return rendered;
}

function lineNames(): string[] {
  return screen.getAllByRole('listitem').map((line) => line.querySelector('h2')?.textContent ?? '');
}

describe('OrderPage', () => {
  beforeEach(() => {
    store = new StoreInMemory();
  });

  // US-10, criterion 1: what was added, on one screen.
  it('shows every drink that was added', async () => {
    await openScreenWith((cart) => {
      cart.add(ginTonic);
      cart.add(fernet);
    });

    expect(lineNames()).toEqual(['Gin Tonic', 'Fernet con Coca']);
  });

  // Queried by selector and not by role: the photo carries an empty alt
  // because the name is right beside it, which makes it decorative and takes
  // it out of the accessibility tree on purpose.
  function thumb(rendered: { container: HTMLElement }): HTMLImageElement | null {
    return rendered.container.querySelector('.thumb');
  }

  // The line has no menu request to fetch a photo from: it shows whatever
  // the cart was carrying, real photo or placeholder.
  it('shows the picture that was carried with the line', async () => {
    const rendered = await openScreenWith((cart) =>
      cart.add({ ...ginTonic, imageUrl: 'https://images.example.com/gin.png' }),
    );

    expect(thumb(rendered)?.src).toBe('https://images.example.com/gin.png');
  });

  it('falls back to the placeholder when the drink has no picture', async () => {
    const rendered = await openScreenWith((cart) => cart.add(ginTonic));

    expect(thumb(rendered)?.src).toContain('product-placeholder');
  });

  it('shows how many of each and what that line costs', async () => {
    await openScreenWith((cart) => {
      cart.add(ginTonic);
      cart.add(ginTonic);
    });

    expect(screen.getByTestId('quantity-Gin Tonic').textContent).toContain('2');
    expect(screen.getByTestId('line-total-Gin Tonic').textContent).toContain('9.000,00');
  });

  // US-10, criterion 2, from the order screen itself.
  it('raises the quantity from the plus', async () => {
    await openScreenWith((cart) => cart.add(ginTonic));

    fireEvent.click(screen.getByLabelText('Agregar otro Gin Tonic'));

    expect(screen.getByTestId('quantity-Gin Tonic').textContent).toContain('2');
  });

  it('lowers the quantity from the minus', async () => {
    await openScreenWith((cart) => {
      cart.add(ginTonic);
      cart.add(ginTonic);
    });

    fireEvent.click(screen.getByLabelText('Quitar un Gin Tonic del pedido'));

    expect(screen.getByTestId('quantity-Gin Tonic').textContent).toContain('1');
  });

  // US-10, criterion 3: the whole drink out, without tapping the minus five
  // times to say so.
  it('takes the whole drink out from "Quitar"', async () => {
    await openScreenWith((cart) => {
      cart.add(ginTonic);
      cart.add(ginTonic);
      cart.add(fernet);
    });

    fireEvent.click(screen.getByLabelText('Sacar Gin Tonic del pedido'));

    expect(lineNames()).toEqual(['Fernet con Coca']);
  });

  // US-10, criterion 5: the aclaración belongs to the drink it was written on.
  it('writes the note onto the drink it was typed on', async () => {
    await openScreenWith((cart) => {
      cart.add(ginTonic);
      cart.add(fernet);
    });

    fireEvent.input(screen.getByLabelText('Nota para Gin Tonic'), {
      target: { value: 'sin hielo' },
    });

    const cart = TestBed.inject(Cart);

    expect(cart.noteOf('id-1')).toBe('sin hielo');
    expect(cart.noteOf('id-2')).toBeNull();
  });

  it('shows the note that was already written', async () => {
    await openScreenWith((cart) => {
      cart.add(ginTonic);
      cart.setNote(ginTonic.id, 'sin hielo');
    });

    expect(screen.getByLabelText<HTMLInputElement>('Nota para Gin Tonic').value).toBe('sin hielo');
  });

  // A note is read at a glance on a ticket in a dark bar; the field stops
  // rather than letting somebody write a paragraph nobody will read.
  it('stops the note where the bar stops reading it', async () => {
    await openScreenWith((cart) => cart.add(ginTonic));

    const field = screen.getByLabelText('Nota para Gin Tonic');

    expect(field.getAttribute('maxlength')).toBe('120');
  });

  it('adds up the order', async () => {
    await openScreenWith((cart) => {
      cart.add(ginTonic);
      cart.add(ginTonic);
      cart.add(fernet);
    });

    expect(screen.getByTestId('subtotal').textContent).toContain('13.000,00');
    expect(screen.getByTestId('total').textContent).toContain('13.000,00');
  });

  it('goes back to the menu of the venue in the address', async () => {
    await openScreenWith((cart) => cart.add(ginTonic));

    expect(screen.getByLabelText('Volver a la carta').getAttribute('href')).toBe('/bar-alfa/menu');
    expect(screen.getByText('Agregar más tragos').closest('a')?.getAttribute('href')).toBe(
      '/bar-alfa/menu',
    );
  });

  // US-11 is the screen this leads to and it does not exist yet. A gold button
  // that does nothing is what reads as a broken app.
  it('cannot be paid yet', async () => {
    await openScreenWith((cart) => cart.add(ginTonic));

    const pay = screen.getByRole<HTMLButtonElement>('button', { name: /Ir a pagar/ });

    expect(pay.disabled).toBe(true);
  });

  describe('with nothing in it', () => {
    it('says so instead of showing an empty list', async () => {
      await openScreenWith();

      expect(screen.queryAllByRole('listitem')).toEqual([]);
      expect(screen.getByRole('status').textContent).toContain('Todavía no agregaste nada');
    });

    it('still adds up, at zero', async () => {
      await openScreenWith();

      expect(screen.getByTestId('total').textContent).toContain('0,00');
    });
  });
});
