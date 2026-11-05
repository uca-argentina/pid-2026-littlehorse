import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { fireEvent, render, screen } from '@testing-library/angular';
import { of, throwError } from 'rxjs';
import { ProblemTypes } from '../../../core/api/problem-types';
import { PRODUCTS_URL } from '../../products/products.service';
import { PROMOTIONS_URL, PromotionsService } from '../promotions.service';
import type { Promotion } from '../promotions.service';
import { PromotionPage } from './promotion.page';

const noAudit = { createdAt: null, createdBy: null, lastModifiedAt: null, lastModifiedBy: null };

const happy: Promotion = {
  id: 'happy',
  name: 'Happy hour',
  discountPercent: 30,
  days: [5],
  startsAt: '23:00:00',
  endsAt: '01:00:00',
  productIds: ['gin'],
  isActive: true,
  audit: noAudit,
};

const products = [
  { id: 'gin', name: 'Gin Tonic', isActive: true },
  { id: 'fernet', name: 'Fernet', isActive: true },
  { id: 'gone', name: 'Dado de baja', isActive: false },
];

let settle: () => Promise<void>;

async function openScreen(id?: string, service: Partial<PromotionsService> = {}) {
  const { fixture } = await render(PromotionPage, {
    inputs: { venueSlug: 'bar-alfa', ...(id === undefined ? {} : { id }) },
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideRouter([{ path: ':venueSlug/staff/promotions', children: [] }]),
      { provide: PromotionsService, useValue: service },
    ],
  });

  const http = TestBed.inject(HttpTestingController);

  http.expectOne(PRODUCTS_URL).flush(products);
  if (id !== undefined) http.expectOne(PROMOTIONS_URL).flush([happy]);
  settle = async () => {
    await fixture.whenStable();
    fixture.detectChanges();
  };
  await TestBed.inject(Router).navigateByUrl('/');
  await settle();
}

async function fill(): Promise<void> {
  fireEvent.input(screen.getByLabelText(/^nombre/i), { target: { value: 'Viernes loco' } });
  fireEvent.input(screen.getByLabelText(/descuento/i), { target: { value: '25' } });
  fireEvent.click(screen.getByLabelText('Viernes'));
  fireEvent.input(screen.getByLabelText(/^desde/i), { target: { value: '23:00' } });
  fireEvent.input(screen.getByLabelText(/^hasta/i), { target: { value: '01:00' } });
  fireEvent.click(screen.getByLabelText('Gin Tonic'));
  await settle();
}

function save(): void {
  fireEvent.click(screen.getByRole('button', { name: /guardar promoción/i }));
}

describe('PromotionPage', () => {
  it('offers only the products still on the menu, and narrows them by what is typed', async () => {
    await openScreen();

    expect(screen.getByLabelText('Gin Tonic')).toBeTruthy();
    expect(screen.queryByLabelText('Dado de baja')).toBeNull();

    fireEvent.input(screen.getByLabelText(/buscar producto/i), { target: { value: 'fer' } });
    await settle();

    expect(screen.queryByLabelText('Gin Tonic')).toBeNull();
    expect(screen.getByLabelText('Fernet')).toBeTruthy();
  });

  it('creates the promotion with its days, window and products, and goes back to the listing', async () => {
    const create = vi.fn().mockReturnValue(of(happy));
    await openScreen(undefined, { create });

    await fill();
    save();

    expect(create).toHaveBeenCalledWith({
      name: 'Viernes loco',
      discountPercent: 25,
      days: [5],
      startsAt: '23:00:00',
      endsAt: '01:00:00',
      productIds: ['gin'],
    });
  });

  it.each([
    [
      'a discount of 0',
      () => fireEvent.input(screen.getByLabelText(/descuento/i), { target: { value: '0' } }),
      /de 1 a 99/i,
    ],
    [
      'a discount of 100',
      () => fireEvent.input(screen.getByLabelText(/descuento/i), { target: { value: '100' } }),
      /de 1 a 99/i,
    ],
    ['no day', () => fireEvent.click(screen.getByLabelText('Viernes')), /al menos un día/i],
    [
      'no product',
      () => fireEvent.click(screen.getByLabelText('Gin Tonic')),
      /al menos un producto/i,
    ],
    [
      'the same start and end',
      () => fireEvent.input(screen.getByLabelText(/^hasta/i), { target: { value: '23:00' } }),
      /no pueden ser iguales/i,
    ],
  ])('stops %s before sending and says why', async (_name, spoil, message) => {
    const create = vi.fn();
    await openScreen(undefined, { create });

    await fill();
    spoil();
    await settle();
    save();
    await settle();

    expect(create).not.toHaveBeenCalled();
    expect(
      screen
        .getAllByRole('alert')
        .map((alert) => alert.textContent)
        .join(' '),
    ).toMatch(message);
  });

  it('says when a product is not on the menu any more', async () => {
    const create = vi.fn().mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 404,
            error: { type: ProblemTypes.promotionProductNotFound },
          }),
      ),
    );
    await openScreen(undefined, { create });

    await fill();
    save();
    await settle();

    expect(screen.getByRole('alert').textContent).toMatch(/ya no está en la carta/i);
  });

  it('opens an existing promotion filled in, and saves it with update', async () => {
    const update = vi.fn().mockReturnValue(of(happy));
    await openScreen('happy', { update });

    expect((screen.getByLabelText(/^nombre/i) as HTMLInputElement).value).toBe('Happy hour');
    expect((screen.getByLabelText(/^desde/i) as HTMLInputElement).value).toBe('23:00');
    expect((screen.getByLabelText('Viernes') as HTMLInputElement).checked).toBe(true);
    expect((screen.getByLabelText('Gin Tonic') as HTMLInputElement).checked).toBe(true);

    fireEvent.click(screen.getByLabelText('Fernet'));
    await settle();
    save();

    expect(update).toHaveBeenCalledWith(
      'happy',
      expect.objectContaining({ name: 'Happy hour', productIds: ['gin', 'fernet'] }),
    );
  });
});
