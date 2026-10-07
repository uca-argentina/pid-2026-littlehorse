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

const noAudit = { createdAt: null, createdBy: null, lastModifiedAt: null, lastModifiedBy: null };

function account(id: string, role: StaffUser['role'], isActive = true): StaffUser {
  return { id, username: id, role, isActive, audit: noAudit };
}

const theTeam: StaffUser[] = [
  account('euge.q', 'Administrator'),
  account('main-bar', 'Kds'),
  account('vip-bar', 'Kds'),
  account('till-1', 'Cashier'),
  account('martin', 'Waiter'),
  account('former-waiter', 'Waiter', false),
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

async function openScreenWithTheTeam(create?: Parameters<typeof openScreen>[0]) {
  const opened = await openScreen(create);

  opened.http.expectOne(STAFF_USERS_URL).flush(theTeam);
  await opened.rendered.fixture.whenStable();

  return opened;
}

function type(label: RegExp, value: string): void {
  fireEvent.input(screen.getByLabelText(label), { target: { value } });
}

function pick(username: string): void {
  screen.getByRole('checkbox', { name: new RegExp(username) }).click();
}

function fillTheNight(): void {
  type(/nombre/i, 'Saturday');
  type(/empieza/i, '2026-10-10T23:00');
  type(/termina/i, '2026-10-11T06:00');
}

async function save(rendered: Awaited<ReturnType<typeof openScreen>>['rendered']): Promise<void> {
  screen.getByRole('button', { name: /crear noche/i }).click();
  await rendered.fixture.whenStable();
}

describe('NewNightPage', () => {
  // US-35: the crew is the venue's KDS, cashiers and waiters. The administrator
  // is never limited by the night, and a deactivated account cannot work it.
  it('offers the active KDS, cashiers and waiters, and nobody else', async () => {
    await openScreenWithTheTeam();

    const offered = screen
      .getAllByRole('checkbox')
      .map((box) => box.closest('label')?.textContent?.trim());

    expect(offered).toEqual(['main-bar', 'vip-bar', 'till-1', 'martin']);
  });

  it('creates the night with its hours and the chosen crew', async () => {
    const { rendered, create } = await openScreenWithTheTeam();

    fillTheNight();
    pick('main-bar');
    pick('till-1');
    pick('martin');
    await save(rendered);

    expect(create).toHaveBeenCalledWith({
      name: 'Saturday',
      startsAt: new Date(2026, 9, 10, 23, 0).toISOString(),
      endsAt: new Date(2026, 9, 11, 6, 0).toISOString(),
      crewIds: ['main-bar', 'till-1', 'martin'],
    });
  });

  it('asks for a KDS before sending anything', async () => {
    const { rendered, create } = await openScreenWithTheTeam();

    fillTheNight();
    pick('till-1');
    await save(rendered);

    expect(screen.getByText('Elegí al menos una KDS.')).not.toBeNull();
    // Also said next to the button: the KDS list may be a long scroll above it.
    expect(screen.getByRole('alert').textContent).toContain('Revisá lo marcado más arriba');
    expect(create).not.toHaveBeenCalled();
  });

  it('asks for a cashier before sending anything', async () => {
    const { rendered, create } = await openScreenWithTheTeam();

    fillTheNight();
    pick('main-bar');
    await save(rendered);

    expect(screen.getByText('Elegí al menos un cajero.')).not.toBeNull();
    expect(create).not.toHaveBeenCalled();
  });

  // A night crosses midnight, so an end "before" the start is the usual typo:
  // the right day was not picked for it.
  it('refuses an end that is not after the start', async () => {
    const { rendered, create } = await openScreenWithTheTeam();

    fillTheNight();
    type(/termina/i, '2026-10-10T06:00');
    pick('main-bar');
    pick('till-1');
    await save(rendered);

    expect(screen.getByText(/tiene que terminar después de empezar/i)).not.toBeNull();
    expect(create).not.toHaveBeenCalled();
  });

  it('says when the hours overlap another night', async () => {
    const overlapping = vi
      .fn()
      .mockReturnValue(
        throwError(
          () => new HttpErrorResponse({ status: 409, error: { type: ProblemTypes.nightOverlaps } }),
        ),
      );
    const { rendered } = await openScreenWithTheTeam(overlapping);

    fillTheNight();
    pick('main-bar');
    pick('till-1');
    await save(rendered);

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

    expect(screen.getAllByRole('checkbox')).toHaveLength(4);
  });
});
