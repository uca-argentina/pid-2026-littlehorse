import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { fireEvent, render, screen } from '@testing-library/angular';
import { of } from 'rxjs';
import { PRODUCTS_URL } from '../../products/products.service';
import { PROMOTIONS_URL, PromotionsService } from '../promotions.service';
import type { Promotion } from '../promotions.service';
import { PromotionsPage } from './promotions.page';

const noAudit = { createdAt: null, createdBy: null, lastModifiedAt: null, lastModifiedBy: null };

function aPromotion(overrides: Partial<Promotion>): Promotion {
  return {
    id: 'happy',
    name: 'Happy hour',
    discountPercent: 30,
    days: [5, 6],
    startsAt: '23:00:00',
    endsAt: '01:00:00',
    productIds: ['gin', 'fernet'],
    isActive: true,
    audit: noAudit,
    ...overrides,
  };
}

const happy = aPromotion({});
const lunch = aPromotion({
  id: 'lunch',
  name: 'Almuerzo',
  discountPercent: 15,
  days: [1, 2, 3, 4, 5, 6, 0],
  startsAt: '12:00:00',
  endsAt: '15:00:00',
  productIds: ['gin'],
  isActive: false,
});

const products = [
  { id: 'gin', name: 'Gin Tonic' },
  { id: 'fernet', name: 'Fernet' },
];

async function openScreen(service: Partial<PromotionsService> = {}) {
  const { fixture } = await render(PromotionsPage, {
    inputs: { venueSlug: 'bar-alfa' },
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideRouter([]),
      { provide: PromotionsService, useValue: service },
    ],
  });

  const http = TestBed.inject(HttpTestingController);

  http.expectOne(PROMOTIONS_URL).flush([happy, lunch]);
  http.expectOne(PRODUCTS_URL).flush(products);
  await fixture.whenStable();
  fixture.detectChanges();

  return { fixture, http };
}

describe('PromotionsPage', () => {
  it('lists each promotion with its discount, days, window, products and state', async () => {
    await openScreen();

    const row = screen.getByRole('row', { name: /happy hour/i });

    expect(row.textContent).toContain('30%');
    expect(row.textContent).toContain('Vie, Sáb');
    expect(row.textContent).toContain('23:00 a 01:00');
    expect(row.textContent).toContain('Gin Tonic, Fernet');
    expect(row.textContent).toContain('Activa');
    expect(screen.getByRole('row', { name: /almuerzo/i }).textContent).toContain('Todos los días');
    expect(screen.getByRole('row', { name: /almuerzo/i }).textContent).toContain('Inactiva');
  });

  it('switches an active promotion off and reloads the list', async () => {
    const deactivate = vi.fn().mockReturnValue(of(happy));
    const { http } = await openScreen({ deactivate });

    fireEvent.click(screen.getByRole('button', { name: /desactivar happy hour/i }));

    expect(deactivate).toHaveBeenCalledWith('happy');
    http.expectOne(PROMOTIONS_URL);
  });

  it('switches an inactive promotion on', async () => {
    const activate = vi.fn().mockReturnValue(of(lunch));
    await openScreen({ activate });

    fireEvent.click(screen.getByRole('button', { name: /activar almuerzo/i }));

    expect(activate).toHaveBeenCalledWith('lunch');
  });

  it('leads to the form for a new one and for each existing one', async () => {
    await openScreen();

    expect(screen.getByRole('link', { name: /nueva promoción/i }).getAttribute('href')).toMatch(
      /\/new$/,
    );
    expect(screen.getByRole('link', { name: /editar happy hour/i }).getAttribute('href')).toMatch(
      /\/happy$/,
    );
  });
});
