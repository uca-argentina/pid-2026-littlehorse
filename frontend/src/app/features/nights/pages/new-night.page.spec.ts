import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { fireEvent, render, screen } from '@testing-library/angular';
import { of, throwError } from 'rxjs';
import { ProblemTypes } from '../../../core/api/problem-types';
import { STAFF_USERS_URL } from '../../staff-users/staff-users.service';
import type { StaffUser } from '../../staff-users/staff-users.service';
import { NightsService } from '../nights.service';
import type { Night } from '../nights.service';
import { NewNightPage } from './new-night.page';

// The form's own rules are NightForm's spec. Here: that the page loads the
// team, sends what the form hands over, and says what the API answered.

const noAudit = { createdAt: null, createdBy: null, lastModifiedAt: null, lastModifiedBy: null };

const theTeam: StaffUser[] = [
  { id: 'main-bar', username: 'main-bar', role: 'Kds', isActive: true, audit: noAudit },
  { id: 'till-1', username: 'till-1', role: 'Cashier', isActive: true, audit: noAudit },
];

const created: Night = {
  id: 'night-1',
  name: 'Saturday',
  startsAt: '',
  endsAt: '',
  crewIds: [],
  audit: noAudit,
};

async function openScreen(create = vi.fn().mockReturnValue(of(created))) {
  const rendered = await render(NewNightPage, {
    inputs: { venueSlug: 'bar-alfa' },
    providers: [
      provideRouter([{ path: ':venueSlug/staff/nights', children: [] }]),
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: NightsService, useValue: { create } },
    ],
  });

  return { rendered, create, http: TestBed.inject(HttpTestingController) };
}

async function fillAndSave(rendered: Awaited<ReturnType<typeof openScreen>>['rendered']) {
  fireEvent.input(screen.getByLabelText(/nombre/i), { target: { value: 'Saturday' } });
  fireEvent.input(screen.getByLabelText(/empieza/i), { target: { value: '2026-10-10T23:00' } });
  fireEvent.input(screen.getByLabelText(/termina/i), { target: { value: '2026-10-11T06:00' } });
  screen.getByRole('button', { name: /^KDS/ }).click();
  screen.getByRole('button', { name: /^Cajeros/ }).click();
  await rendered.fixture.whenStable();
  screen.getByRole('checkbox', { name: 'main-bar' }).click();
  screen.getByRole('checkbox', { name: 'till-1' }).click();
  screen.getByRole('button', { name: /crear noche/i }).click();
  await rendered.fixture.whenStable();
}

describe('NewNightPage', () => {
  it('creates the night the form hands over', async () => {
    const { rendered, create, http } = await openScreen();
    http.expectOne(STAFF_USERS_URL).flush(theTeam);
    await rendered.fixture.whenStable();

    await fillAndSave(rendered);

    expect(create).toHaveBeenCalledWith(
      expect.objectContaining({ name: 'Saturday', crewIds: ['main-bar', 'till-1'] }),
    );
  });

  it('says when the hours overlap another night', async () => {
    const overlapping = vi
      .fn()
      .mockReturnValue(
        throwError(
          () => new HttpErrorResponse({ status: 409, error: { type: ProblemTypes.nightOverlaps } }),
        ),
      );
    const { rendered, http } = await openScreen(overlapping);
    http.expectOne(STAFF_USERS_URL).flush(theTeam);
    await rendered.fixture.whenStable();

    await fillAndSave(rendered);

    expect(screen.getByRole('alert').textContent).toContain('se superpone con otra noche');
  });

  it('says so when the team cannot be loaded, and tries again', async () => {
    const { rendered, http } = await openScreen();

    http.expectOne(STAFF_USERS_URL).flush('', { status: 500, statusText: 'Server Error' });
    await rendered.fixture.whenStable();

    expect(screen.getByRole('alert').textContent).toContain('No pudimos traer al equipo');

    screen.getByRole('button', { name: /reintentar/i }).click();
    TestBed.tick();
    http.expectOne(STAFF_USERS_URL).flush(theTeam);
    await rendered.fixture.whenStable();

    expect(screen.getByLabelText(/nombre/i)).not.toBeNull();
  });
});
