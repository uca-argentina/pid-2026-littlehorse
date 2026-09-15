import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
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
    providers: [provideHttpClient(), provideHttpClientTesting()],
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

describe('MenuPage', () => {
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
});
