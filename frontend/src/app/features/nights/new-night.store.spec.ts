import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { ProblemTypes } from '../../core/api/problem-types';
import { NewNightStore } from './new-night.store';
import { NightsService } from './nights.service';
import type { NewNight, Night } from './nights.service';

const noAudit = { createdAt: null, createdBy: null, lastModifiedAt: null, lastModifiedBy: null };

function rejectedWith(status: number, type: string): HttpErrorResponse {
  return new HttpErrorResponse({ status, error: { type } });
}

const aNewNight: NewNight = {
  name: 'Saturday 10/10',
  startsAt: '2026-10-11T02:00:00.000Z',
  endsAt: '2026-10-11T09:00:00.000Z',
  crewIds: ['kds-1', 'till-1'],
};

const created: Night = { id: 'night-1', ...aNewNight, audit: noAudit };

describe('NewNightStore', () => {
  let store: NewNightStore;
  let create: ReturnType<typeof vi.fn>;
  let router: Router;

  beforeEach(() => {
    create = vi.fn();

    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        NewNightStore,
        { provide: NightsService, useValue: { create } },
      ],
    });

    store = TestBed.inject(NewNightStore);
    router = TestBed.inject(Router);
  });

  it('does not send twice while a request is in flight', () => {
    create.mockReturnValue(new Subject<Night>());

    store.submit('bar-alfa', aNewNight);
    store.submit('bar-alfa', aNewNight);

    expect(create).toHaveBeenCalledTimes(1);
  });

  // Criterion 1: the night "queda en el listado".
  it('goes back to the listing once the night exists', () => {
    create.mockReturnValue(of(created));
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    store.submit('bar-alfa', aNewNight);

    expect(navigate).toHaveBeenCalledWith(['bar-alfa', 'staff', 'nights']);
    expect(store.status()).toBe('idle');
  });

  // The one failure the administrator can fix on the spot, by moving the hours.
  it('says the hours overlap another night when the API refuses them', () => {
    create.mockReturnValue(throwError(() => rejectedWith(409, ProblemTypes.nightOverlaps)));

    store.submit('bar-alfa', aNewNight);

    expect(store.status()).toBe('overlaps');
  });

  // An account deactivated or reassigned while the form was open.
  it('says the crew changed when one of the accounts is no longer the venue’s', () => {
    create.mockReturnValue(
      throwError(() => rejectedWith(400, ProblemTypes.nightCrewMemberNotFound)),
    );

    store.submit('bar-alfa', aNewNight);

    expect(store.status()).toBe('crewChanged');
  });

  it('falls back to a single failure for anything else', () => {
    create.mockReturnValue(throwError(() => rejectedWith(500, 'about:blank')));

    store.submit('bar-alfa', aNewNight);

    expect(store.status()).toBe('unreachable');
  });

  it('clears a previous failure when the next attempt starts', () => {
    create.mockReturnValueOnce(throwError(() => rejectedWith(409, ProblemTypes.nightOverlaps)));
    store.submit('bar-alfa', aNewNight);

    create.mockReturnValueOnce(new Subject<Night>());
    store.submit('bar-alfa', aNewNight);

    expect(store.status()).toBe('sending');
  });
});
