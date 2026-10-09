import { expect } from '@playwright/test';
import type { APIRequestContext } from '@playwright/test';

interface NightBody {
  readonly id: string;
  readonly name: string;
  readonly startsAt: string;
  readonly endsAt: string;
  readonly crewIds: string[];
}

/**
 * US-35, criterion 4: a KDS or a till only sees the venue's orders while it
 * works the night. Every account a spec creates joins the night that
 * night.setup.ts left on, the way an administrator would add it from the
 * night's screen.
 */
export async function joinTonight(
  request: APIRequestContext,
  adminToken: string,
  staffUserId: string,
): Promise<void> {
  const headers = { Authorization: `Bearer ${adminToken}` };

  // Specs create accounts in parallel, and a night is saved whole: two joins
  // that read the same crew make the second one drop the first. So it reads
  // again after saving, and joins again until the account stays in.
  for (let attempt = 0; attempt < 5; attempt++) {
    const tonight = await tonightsNight(request, headers);

    if (tonight.crewIds.includes(staffUserId)) return;

    const joined = await request.put(`/api/nights/${tonight.id}`, {
      headers,
      data: { ...tonight, crewIds: [...tonight.crewIds, staffUserId] },
    });

    expect(joined.ok()).toBe(true);
  }

  expect((await tonightsNight(request, headers)).crewIds).toContain(staffUserId);
}

/** The listing is the latest first: the first one that started is tonight's. */
async function tonightsNight(
  request: APIRequestContext,
  headers: Record<string, string>,
): Promise<NightBody> {
  const now = Date.now();
  const nights = (await (await request.get('/api/nights', { headers })).json()) as NightBody[];
  const tonight = nights.find((night) => Date.parse(night.startsAt) <= now);

  expect(tonight, 'night.setup.ts should have left a night on').toBeDefined();

  return tonight!;
}
