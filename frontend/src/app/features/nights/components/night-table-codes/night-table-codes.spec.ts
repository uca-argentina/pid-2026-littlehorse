import { HttpErrorResponse } from '@angular/common/http';
import { By } from '@angular/platform-browser';
import { render, screen, within } from '@testing-library/angular';
import { QRCodeComponent } from 'angularx-qrcode';
import { of, throwError } from 'rxjs';
import { NightsService } from '../../nights.service';
import type { NightTable } from '../../nights.service';
import { NightTableCodes } from './night-table-codes';

const FIVE_CODE = 'a'.repeat(32);
const NEW_CODE = 'b'.repeat(32);

const tableFive: NightTable = {
  tableId: 't5',
  number: 5,
  capacity: 10,
  location: 'Al lado de la cabina',
  budget: 150000,
  spent: 40000,
  accessCode: FIVE_CODE,
};

async function openCodes(
  regenerateTableCode = vi.fn().mockReturnValue(of({ tableId: 't5', accessCode: NEW_CODE })),
  phase: 'upcoming' | 'underway' | 'over' = 'underway',
) {
  const rendered = await render(NightTableCodes, {
    inputs: { venueSlug: 'bar-alfa', nightId: 'night-1', phase, tables: [tableFive] },
    providers: [{ provide: NightsService, useValue: { regenerateTableCode } }],
  });

  return { rendered, regenerateTableCode };
}

function qrData(rendered: Awaited<ReturnType<typeof openCodes>>['rendered']): string {
  const qr = rendered.fixture.debugElement.query(By.directive(QRCodeComponent));

  return (qr.componentInstance as QRCodeComponent).qrdata;
}

describe('NightTableCodes', () => {
  // §8.2: the QR opens the menu of that table, for whoever the waiter hands it to.
  it('shows each table with a QR that opens its menu', async () => {
    const { rendered } = await openCodes();

    expect(screen.getByRole('heading', { name: 'Mesa 5' })).not.toBeNull();
    expect(qrData(rendered)).toBe(`${location.origin}/bar-alfa/table/${FIVE_CODE}`);
  });

  it('says how much of the budget is left', async () => {
    await openCodes();

    expect(screen.getByText(/quedan \$ 110\.000,00 de \$ 150\.000,00/i)).not.toBeNull();
  });

  // Giving a new code cuts off whoever has the old one: it asks first.
  it('asks before giving a table a new code', async () => {
    const { rendered, regenerateTableCode } = await openCodes();

    screen.getByRole('button', { name: /código nuevo para la mesa 5/i }).click();
    await rendered.fixture.whenStable();

    expect(regenerateTableCode).not.toHaveBeenCalled();
    expect(screen.getByText(/el código de ahora deja de servir/i)).not.toBeNull();
  });

  it('gives the table a new code once confirmed, and shows its QR', async () => {
    const { rendered, regenerateTableCode } = await openCodes();

    screen.getByRole('button', { name: /código nuevo para la mesa 5/i }).click();
    await rendered.fixture.whenStable();
    screen.getByRole('button', { name: /sí, generar otro/i }).click();
    await rendered.fixture.whenStable();

    expect(regenerateTableCode).toHaveBeenCalledWith('night-1', 't5');
    expect(qrData(rendered)).toBe(`${location.origin}/bar-alfa/table/${NEW_CODE}`);
  });

  it('says so when the new code could not be made', async () => {
    const { rendered } = await openCodes(
      vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status: 0 }))),
    );

    screen.getByRole('button', { name: /código nuevo para la mesa 5/i }).click();
    await rendered.fixture.whenStable();
    screen.getByRole('button', { name: /sí, generar otro/i }).click();
    await rendered.fixture.whenStable();

    expect(within(screen.getByRole('alert')).getByText(/no pudimos/i)).not.toBeNull();
    expect(qrData(rendered)).toBe(`${location.origin}/bar-alfa/table/${FIVE_CODE}`);
  });

  // A finished night's codes no longer open anything.
  it('shows no codes once the night is over', async () => {
    await openCodes(undefined, 'over');

    expect(screen.queryByRole('button', { name: /código nuevo/i })).toBeNull();
    expect(screen.queryByRole('heading', { name: 'Mesa 5' })).toBeNull();
  });
});
