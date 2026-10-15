import { TestBed } from '@angular/core/testing';
import { HUB_CONNECTION } from '../../core/realtime/hub-channel';
import type { HubConnectionLike } from '../../core/realtime/hub-channel';
import { WaiterChannel } from './waiter-channel';

/** The till's own hub: what the rest of HubChannel does is covered by KdsBoardChannel's spec. */
describe('WaiterChannel', () => {
  it('listens to the waiter’s hub for its one message', () => {
    let url = '';
    const listening: string[] = [];
    const connection: HubConnectionLike = {
      on: (method) => listening.push(method),
      onreconnecting: () => undefined,
      onreconnected: () => undefined,
      onclose: () => undefined,
      start: () => Promise.resolve(),
      stop: () => Promise.resolve(),
      invoke: () => Promise.resolve(),
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

    TestBed.inject(WaiterChannel).connect(() => undefined);

    expect(url).toBe('/api/hubs/waiter');
    expect(listening).toEqual(['TablesChanged']);
  });
});
