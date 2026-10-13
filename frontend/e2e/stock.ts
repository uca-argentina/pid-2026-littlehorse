import { expect } from '@playwright/test';
import type { APIRequestContext } from '@playwright/test';
import { tonightsNight } from './nights';

interface StaffBody {
  readonly id: string;
  readonly username: string;
  readonly role: string;
  readonly isActive: boolean;
}

/**
 * US-37: what a customer orders is taken from a KDS of tonight's night, and
 * the menu only offers what one of them holds. A product a spec creates comes
 * after the night was set up, so none of it was split among its KDS: this
 * moves its units from the storeroom into one of them, the way an
 * administrator does it from the night's screen.
 *
 * The KDS named by the spec when its board has to see the order; otherwise
 * the first active KDS of tonight's crew, the one night.setup.ts put there, so
 * no other spec's board gets orders it did not ask for.
 */
export async function loadIntoTonight(
  request: APIRequestContext,
  adminToken: string,
  productId: string,
  units: number,
  kdsUsername?: string,
): Promise<void> {
  if (units <= 0) return;

  const headers = { Authorization: `Bearer ${adminToken}` };
  const tonight = await tonightsNight(request, headers);
  const staff = (await (await request.get('/api/staff/users', { headers })).json()) as StaffBody[];

  const kds =
    kdsUsername === undefined
      ? tonight.crewIds
          .map((id) => staff.find((user) => user.id === id))
          .find((user) => user?.role === 'Kds' && user.isActive)
      : staff.find((user) => user.username === kdsUsername);

  expect(kds, 'tonight should have a KDS to load the stock into').toBeDefined();

  const loaded = await request.post(
    `/api/nights/${tonight.id}/stock/${kds!.id}/${productId}/adjust`,
    { headers, data: { change: units } },
  );

  expect(loaded.ok()).toBe(true);
}
