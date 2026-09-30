import { TestBed } from '@angular/core/testing';
import { HUB_CONNECTION } from '../../core/realtime/hub-channel';
import type { HubConnectionLike } from '../../core/realtime/hub-channel';
import { TillChannel } from './till-channel';

/** The till's own hub: what the rest of HubChannel does is covered by KdsBoardChannel's spec. */
describe('TillChannel', () => {
  it('listens to the till’s hub for its one message', () => {
    let url = '';
    const listening: string[] = [];
    const connection: HubConnectionLike = {
      on: (method) => listening.push(method),
      onreconnecting: () => undefined,
      onreconnected: () => undefined,
      onclose: () => undefined,
      start: () => Promise.resolve(),
      stop: () => Promise.resolve(),
    };

    TestBed.configureTestingModule({
      providers: [
        {
          provide: HUB_CONNECTION,
          useValue: (hubUrl: string) => {
            url = hubUrl;
            return connection;
          },
        },
      ],
    });

    TestBed.inject(TillChannel).connect(() => undefined);

    expect(url).toBe('/api/hubs/till');
    expect(listening).toEqual(['TillChanged']);
  });
});
