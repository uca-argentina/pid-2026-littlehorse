import { expect } from '@playwright/test';
import type { APIRequestContext } from '@playwright/test';

/**
 * The id of one of the venue's categories, by the name it is shown under. The
 * seeded venue starts with Tragos, Cervezas and Sin alcohol; a spec that loads
 * a product straight through the API needs one of them to file it under.
 */
export async function categoryIdNamed(
  request: APIRequestContext,
  token: string,
  name: string,
): Promise<string> {
  const listed = await request.get('/api/staff/categories', {
    headers: { Authorization: `Bearer ${token}` },
  });

  expect(listed.status()).toBe(200);

  const category = ((await listed.json()) as { id: string; name: string }[]).find(
    (row) => row.name === name,
  );

  expect(category, `the seeded venue has a category named "${name}"`).toBeDefined();

  return category!.id;
}
