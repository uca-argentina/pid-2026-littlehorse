import { expect, test as setup } from '@playwright/test';
import type { APIRequestContext } from '@playwright/test';
import { seededAdminPassword, seededAdminUsername, seededVenueSlug } from './seeded-data';

/**
 * US-35: with no night on, the menu reads but nothing can be ordered, and
 * every spec that orders would fail for a reason none of them is about. This
 * runs once, before them, and leaves the seeded venue with a night on.
 *
 * The development database keeps everything every run made, so this reuses a
 * night that is already on, and never creates one that overlaps another.
 */
const HOUR = 60 * 60 * 1000;

/** Long enough for any run, short enough to leave tomorrow's real nights alone. */
const NIGHT_LENGTH = 12 * HOUR;

interface NightBody {
  readonly id: string;
  readonly name: string;
  readonly startsAt: string;
  readonly endsAt: string;
  readonly crewIds: string[];
}

interface StaffBody {
  readonly id: string;
  readonly role: string;
  readonly isActive: boolean;
}

async function adminHeaders(request: APIRequestContext): Promise<Record<string, string>> {
  const login = await request.post(`/api/${seededVenueSlug}/auth/login`, {
    data: { username: seededAdminUsername, password: seededAdminPassword() },
  });
  expect(login.ok()).toBe(true);
  const { token } = (await login.json()) as { token: string };

  return { Authorization: `Bearer ${token}` };
}

/** An active account of that role, made for this purpose when the venue has none. */
async function anAccount(
  request: APIRequestContext,
  headers: Record<string, string>,
  staff: StaffBody[],
  role: 'Kds' | 'Cashier',
): Promise<string> {
  const existing = staff.find((user) => user.role === role && user.isActive);

  if (existing) return existing.id;

  const created = await request.post('/api/staff/users', {
    headers,
    data: {
      username: `e2e.night${role.toLowerCase()}${Date.now().toString(36)}`,
      password: 'a-long-enough-password',
      role,
    },
  });
  expect(created.status()).toBe(201);

  return ((await created.json()) as StaffBody).id;
}

setup('a night is on for the seeded venue', async ({ request }) => {
  const headers = await adminHeaders(request);
  const now = Date.now();

  const nights = (await (await request.get('/api/nights', { headers })).json()) as NightBody[];
  const on = nights.find(
    (night) => Date.parse(night.startsAt) <= now && now < Date.parse(night.endsAt),
  );
  const nextStart = Math.min(
    ...nights.map((night) => Date.parse(night.startsAt)).filter((start) => start > now),
    now + NIGHT_LENGTH,
  );

  if (on) {
    if (Date.parse(on.endsAt) - now > HOUR) return;

    // On, but about to close mid-run: extend it as far as the next night allows.
    const extended = await request.put(`/api/nights/${on.id}`, {
      headers,
      data: { ...on, endsAt: new Date(nextStart).toISOString() },
    });
    expect(extended.ok()).toBe(true);

    return;
  }

  // Not before the end of the last night, which may have ended a second ago.
  const lastEnd = Math.max(
    ...nights.map((night) => Date.parse(night.endsAt)).filter((end) => end <= now),
    now - 60_000,
  );
  const staff = (await (await request.get('/api/staff/users', { headers })).json()) as StaffBody[];

  const created = await request.post('/api/nights', {
    headers,
    data: {
      name: 'E2E',
      startsAt: new Date(lastEnd).toISOString(),
      endsAt: new Date(nextStart).toISOString(),
      crewIds: [
        await anAccount(request, headers, staff, 'Kds'),
        await anAccount(request, headers, staff, 'Cashier'),
      ],
    },
  });
  expect(created.status()).toBe(201);
});
