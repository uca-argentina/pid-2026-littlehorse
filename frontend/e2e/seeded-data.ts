import { readFileSync } from 'node:fs';
import { join } from 'node:path';

/**
 * The venue and the administrator that DevelopmentSeeder writes when the API
 * starts in Development. The defaults live in DevelopmentSeedOptions.cs.
 */
export const seededVenueSlug = 'bar-alfa';
export const seededAdminUsername = 'admin';

const launchSettingsPath = join(
  __dirname,
  '..',
  '..',
  'backend',
  'src',
  'DrinkIt.Api',
  'Properties',
  'launchSettings.json',
);

interface LaunchSettings {
  profiles: Record<string, { environmentVariables?: Record<string, string> }>;
}

/**
 * Read from the API's own launch profile rather than repeated here. Two copies
 * of the same password drift, and the run then fails looking like a broken
 * login screen instead of stale configuration.
 */
export function seededAdminPassword(): string {
  const settings = JSON.parse(readFileSync(launchSettingsPath, 'utf8')) as LaunchSettings;
  const password =
    settings.profiles['https']?.environmentVariables?.['DevelopmentSeed__AdminPassword'];

  if (!password)
    throw new Error(
      `DevelopmentSeed__AdminPassword is missing from the "https" profile in ${launchSettingsPath}.`,
    );

  return password;
}
