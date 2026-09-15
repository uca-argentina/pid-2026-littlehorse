import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { render, screen } from '@testing-library/angular';
import { PRODUCTS_URL } from '../products.service';
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

    expect(screen.getByText(/sin stock/i)).not.toBeNull();
  });

  // US-08 keeps deactivated products in the listing so old orders still point
  // somewhere. Showing them identical to the rest would be worse than hiding
  // them: the administrator would think customers can still see them.
  it('marks whatever was deactivated instead of hiding it', async () => {
    await openScreenShowing(theMenu);

    expect(screen.getByText(/dado de baja/i)).not.toBeNull();
  });

  it('shows the picture when there is one and a placeholder when there is not', async () => {
    await openScreenShowing(theMenu);

    const pictures = screen.getAllByRole('img');

    expect(pictures).toHaveLength(1);
    expect(pictures[0]?.getAttribute('src')).toBe('https://images.example.com/gin-tonic.jpg');
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
});
