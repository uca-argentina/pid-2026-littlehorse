import { TestBed } from '@angular/core/testing';
import { BrowserStore } from '../storage/browser-store';
import { CART_STORAGE_PREFIX, Cart } from './cart';

const ginTonic = { id: 'id-1', name: 'Gin Tonic', price: 4500 };
const fernet = { id: 'id-2', name: 'Fernet con Coca', price: 4000 };

/**
 * What the browser would remember, as a Map. The runner is Node and there is
 * no localStorage there, so a spec that reached for one would fail for a reason
 * that has nothing to do with the cart.
 */
class StoreInMemory extends BrowserStore {
  readonly entries = new Map<string, string>();

  override read(key: string): string | null {
    return this.entries.get(key) ?? null;
  }

  override write(key: string, value: string): void {
    this.entries.set(key, value);
  }
}

/**
 * A browser that forgets: a private window, a full quota. BrowserStore turns
 * every one of those into a write that goes nowhere, which is what reaches the
 * cart.
 */
class StoreThatForgets extends BrowserStore {
  override read(): string | null {
    return null;
  }

  override write(): void {
    // Swallowed, exactly as the real one does.
  }
}

let store: StoreInMemory;

/** A cart that survived the app being closed keeps whatever the store holds. */
function aCart(browserStore: BrowserStore = store): Cart {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    providers: [{ provide: BrowserStore, useValue: browserStore }],
  });

  return TestBed.inject(Cart);
}

describe('Cart', () => {
  beforeEach(() => {
    store = new StoreInMemory();
  });

  it('starts with nothing in it', () => {
    const cart = aCart();
    cart.open('bar-alfa');

    expect(cart.isEmpty()).toBe(true);
    expect(cart.count()).toBe(0);
    expect(cart.total()).toBe(0);
  });

  // US-10, criterion 1.
  it('puts what was added into the order', () => {
    const cart = aCart();
    cart.open('bar-alfa');

    cart.add(ginTonic);

    expect(cart.lines()).toEqual([
      { productId: 'id-1', name: 'Gin Tonic', unitPrice: 4500, quantity: 1, note: null },
    ]);
  });

  // Two taps on the same card mean two of that drink, not two entries for it:
  // the cart screen shows one line with a quantity beside it.
  it('raises the quantity instead of adding the same drink twice', () => {
    const cart = aCart();
    cart.open('bar-alfa');

    cart.add(ginTonic);
    cart.add(ginTonic);

    expect(cart.lines()).toHaveLength(1);
    expect(cart.lines()[0].quantity).toBe(2);
  });

  it('counts every unit and not every line', () => {
    const cart = aCart();
    cart.open('bar-alfa');

    cart.add(ginTonic);
    cart.add(ginTonic);
    cart.add(fernet);

    expect(cart.count()).toBe(3);
  });

  it('adds up quantity by price', () => {
    const cart = aCart();
    cart.open('bar-alfa');

    cart.add(ginTonic);
    cart.add(ginTonic);
    cart.add(fernet);

    expect(cart.total()).toBe(13000);
  });

  // US-10, criterion 4: somebody takes a phone call and comes back.
  it('is still there when the app is opened again', () => {
    const before = aCart();
    before.open('bar-alfa');
    before.add(ginTonic);
    before.add(ginTonic);

    const after = aCart();
    after.open('bar-alfa');

    expect(after.count()).toBe(2);
    expect(after.total()).toBe(9000);
  });

  // Somebody who goes to another bar the same night starts a new order there,
  // and the one they left behind is untouched.
  it('keeps the order of one venue out of another', () => {
    const cart = aCart();
    cart.open('bar-alfa');
    cart.add(ginTonic);

    cart.open('bar-beta');

    expect(cart.isEmpty()).toBe(true);

    cart.open('bar-alfa');

    expect(cart.count()).toBe(1);
  });

  it('files each venue under its own key', () => {
    const cart = aCart();
    cart.open('bar-alfa');
    cart.add(ginTonic);

    expect(store.entries.has(`${CART_STORAGE_PREFIX}bar-alfa`)).toBe(true);
    expect(store.entries.has(`${CART_STORAGE_PREFIX}bar-beta`)).toBe(false);
  });

  /**
   * A private window, a browser set to block site data, storage that is full.
   * Losing the order is bad; a menu that will not render at all is worse.
   */
  /**
   * Losing the order when the tab closes is bad; a menu that refuses to work at
   * all is worse. What is on screen this visit still holds.
   */
  it('still works when the browser forgets everything', () => {
    const cart = aCart(new StoreThatForgets());

    cart.open('bar-alfa');
    cart.add(ginTonic);

    expect(cart.count()).toBe(1);
    expect(cart.total()).toBe(4500);
  });

  it('starts clean when what was stored cannot be read back', () => {
    store.entries.set(`${CART_STORAGE_PREFIX}bar-alfa`, 'no es json');

    const cart = aCart();
    cart.open('bar-alfa');

    expect(cart.isEmpty()).toBe(true);
  });

  describe('taking things out', () => {
    // US-10, criterion 2, done from the menu card itself.
    it('lowers the quantity by one', () => {
      const cart = aCart();
      cart.open('bar-alfa');
      cart.add(ginTonic);
      cart.add(ginTonic);

      cart.subtract(ginTonic.id);

      expect(cart.quantityOf(ginTonic.id)).toBe(1);
      expect(cart.total()).toBe(4500);
    });

    // The last one taken out is the drink leaving the order, not a line sitting
    // there at zero: a card showing "0" alongside a minus is a dead control.
    it('takes the drink out of the order when the last one goes', () => {
      const cart = aCart();
      cart.open('bar-alfa');
      cart.add(ginTonic);

      cart.subtract(ginTonic.id);

      expect(cart.lines()).toEqual([]);
      expect(cart.isEmpty()).toBe(true);
    });

    it('leaves the rest of the order alone', () => {
      const cart = aCart();
      cart.open('bar-alfa');
      cart.add(ginTonic);
      cart.add(fernet);

      cart.subtract(ginTonic.id);

      expect(cart.quantityOf(fernet.id)).toBe(1);
    });

    // Two taps racing on a slow phone, or a stale screen.
    it('does nothing to a drink that is not in the order', () => {
      const cart = aCart();
      cart.open('bar-alfa');
      cart.add(ginTonic);

      cart.subtract('id-nunca-agregado');

      expect(cart.count()).toBe(1);
    });

    it('is still gone when the app is opened again', () => {
      const before = aCart();
      before.open('bar-alfa');
      before.add(ginTonic);
      before.subtract(ginTonic.id);

      const after = aCart();
      after.open('bar-alfa');

      expect(after.isEmpty()).toBe(true);
    });
  });

  it('answers how many of a drink are in the order', () => {
    const cart = aCart();
    cart.open('bar-alfa');
    cart.add(ginTonic);
    cart.add(ginTonic);

    expect(cart.quantityOf(ginTonic.id)).toBe(2);
    expect(cart.quantityOf(fernet.id)).toBe(0);
  });
});
