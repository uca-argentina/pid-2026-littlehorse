import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { fireEvent, render, screen } from '@testing-library/angular';
import { of, throwError } from 'rxjs';
import { ProblemTypes } from '../../../core/api/problem-types';
import { PRODUCTS_URL } from '../../products/products.service';
import { STAMP_CARD_RULE_URL, StampCardRuleService } from '../stamp-card-rule.service';
import type { StampCardRule } from '../stamp-card-rule.service';
import { StampCardPage } from './stamp-card.page';

const noAudit = { createdAt: null, createdBy: null, lastModifiedAt: null, lastModifiedBy: null };

const rule: StampCardRule = {
  stampsForReward: 8,
  rewardProductId: 'fernet',
  isActive: true,
  audit: noAudit,
};

const products = [
  { id: 'gin', name: 'Gin Tonic', isActive: true, isCombo: false },
  { id: 'fernet', name: 'Fernet', isActive: true, isCombo: false },
  { id: 'combo', name: 'Combo Gin', isActive: true, isCombo: true },
  { id: 'gone', name: 'Dado de baja', isActive: false, isCombo: false },
];

let settle: () => Promise<void>;

async function openScreen(
  existing: StampCardRule | null,
  service: Partial<StampCardRuleService> = {},
) {
  const { fixture } = await render(StampCardPage, {
    inputs: { venueSlug: 'bar-alfa' },
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideRouter([{ path: '**', children: [] }]),
      { provide: StampCardRuleService, useValue: service },
    ],
  });

  const http = TestBed.inject(HttpTestingController);

  http.expectOne(PRODUCTS_URL).flush(products);
  http
    .expectOne(STAMP_CARD_RULE_URL)
    .flush(existing, existing === null ? { status: 204, statusText: 'No Content' } : {});
  settle = async () => {
    await fixture.whenStable();
    fixture.detectChanges();
  };
  await TestBed.inject(Router).navigateByUrl('/');
  await settle();
}

function stamps(): HTMLInputElement {
  return screen.getByLabelText<HTMLInputElement>(/sellos para el regalo/i);
}

function reward(): HTMLSelectElement {
  return screen.getByLabelText<HTMLSelectElement>(/producto de regalo/i);
}

function save(): void {
  fireEvent.click(screen.getByRole('button', { name: /guardar tarjeta/i }));
}

describe('StampCardPage', () => {
  // US-57: "sin este alta, no hay tarjeta".
  it('starts empty when the venue has no card yet', async () => {
    await openScreen(null);

    expect(stamps().value).toBe('5');
    expect(reward().value).toBe('');
    expect(screen.getByText(/todavía no tenés una tarjeta de sellos/i)).toBeTruthy();
  });

  it('shows the rule the venue has', async () => {
    await openScreen(rule);

    expect(stamps().value).toBe('8');
    expect(reward().value).toBe('fernet');
    expect(screen.getByLabelText<HTMLInputElement>(/tarjeta activa/i).checked).toBe(true);
  });

  // US-57, criterion 1: the reward is a product that is not a combo.
  it('offers only products still on the menu that are not combos', async () => {
    await openScreen(null);

    const names = [...reward().options].map((option) => option.textContent.trim());

    expect(names).toContain('Gin Tonic');
    expect(names).toContain('Fernet');
    expect(names).not.toContain('Combo Gin');
    expect(names).not.toContain('Dado de baja');
  });

  // US-57, criterion 1: two or more.
  it('refuses fewer than two stamps without asking the server', async () => {
    const save$ = vi.fn();
    await openScreen(rule, { save: save$ });

    fireEvent.input(stamps(), { target: { value: '1' } });
    await settle();
    save();
    await settle();

    expect(screen.getByText(/de 2 a 50/i)).toBeTruthy();
    expect(save$).not.toHaveBeenCalled();
  });

  it('asks for a reward before saving', async () => {
    const save$ = vi.fn();
    await openScreen(null, { save: save$ });

    save();
    await settle();

    expect(screen.getByText(/elegí el producto de regalo/i)).toBeTruthy();
    expect(save$).not.toHaveBeenCalled();
  });

  it('saves the rule with the switch as it is', async () => {
    const save$ = vi.fn().mockReturnValue(of(rule));
    await openScreen(null, { save: save$ });

    fireEvent.input(stamps(), { target: { value: '6' } });
    fireEvent.change(reward(), { target: { value: 'gin' } });
    await settle();
    save();
    await settle();

    expect(save$).toHaveBeenCalledWith({
      stampsForReward: 6,
      rewardProductId: 'gin',
      isActive: true,
    });
    expect(screen.getByRole('status').textContent).toContain('Guardamos la tarjeta');
  });

  // US-57, criterion 3: off keeps the rule and the stamps.
  it('can switch the card off and says the customers keep their stamps', async () => {
    const save$ = vi.fn().mockReturnValue(of({ ...rule, isActive: false }));
    await openScreen(rule, { save: save$ });

    fireEvent.click(screen.getByLabelText(/tarjeta activa/i));
    await settle();
    save();
    await settle();

    expect(save$).toHaveBeenCalledWith({
      stampsForReward: 8,
      rewardProductId: 'fernet',
      isActive: false,
    });
    expect(screen.getByRole('status').textContent).toContain('conservan sus sellos');
  });

  it('shows what the customer will see, with the numbers typed', async () => {
    await openScreen(rule);

    fireEvent.input(stamps(), { target: { value: '10' } });
    await settle();

    expect(screen.getByTestId('stamps-progress').textContent).toContain('0 de 10 sellos');
    expect(screen.getByText(/te regalamos un Fernet/)).toBeTruthy();
  });

  it('says in Spanish when the reward is not on the menu any more', async () => {
    const save$ = vi.fn().mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 404,
            error: { type: ProblemTypes.stampCardRewardProductNotFound },
          }),
      ),
    );
    await openScreen(rule, { save: save$ });

    save();
    await settle();

    expect(screen.getByRole('alert').textContent).toContain('ya no está en la carta');
  });

  it('says in Spanish when the reward turned out to be a combo', async () => {
    const save$ = vi.fn().mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 400,
            error: { type: ProblemTypes.stampCardRewardIsACombo },
          }),
      ),
    );
    await openScreen(rule, { save: save$ });

    save();
    await settle();

    expect(screen.getByRole('alert').textContent).toContain('combo');
  });

  it('says it could not save when the server cannot be reached', async () => {
    const save$ = vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status: 0 })));
    await openScreen(rule, { save: save$ });

    save();
    await settle();

    expect(screen.getByRole('alert').textContent).toContain('No pudimos guardarla');
  });
});
