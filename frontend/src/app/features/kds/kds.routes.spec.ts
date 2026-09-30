import { authenticatedGuard } from '../../core/auth/authenticated-guard';
import { kdsGuard } from '../../core/auth/kds-guard';
import { kdsRoutes } from './kds.routes';

describe('kdsRoutes', () => {
  // The scan screen hands orders over: it is the station's, same as the board.
  it('guards the scan screen like the board', () => {
    const board = kdsRoutes.find((route) => route.path === '');
    const scan = kdsRoutes.find((route) => route.path === 'scan');

    expect(scan?.canActivate).toEqual([authenticatedGuard, kdsGuard]);
    expect(scan?.canActivate).toEqual(board?.canActivate);
  });
});
