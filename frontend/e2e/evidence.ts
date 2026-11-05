import { join } from 'node:path';
import type { Page } from '@playwright/test';

/**
 * A picture of the screen for a sprint's delivery, only when asked for with
 * EVIDENCE_DIR: a normal run leaves nothing behind.
 */
export async function evidence(page: Page, name: string): Promise<void> {
  const directory = process.env['EVIDENCE_DIR'];

  if (directory === undefined) return;

  await page.screenshot({ path: join(directory, `${name}.png`), fullPage: true });
}
