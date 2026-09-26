import { render, screen } from '@testing-library/angular';
import type { components } from '../../core/api/schema';
import { AuditNote } from './audit-note';

type Audit = components['schemas']['AuditResponse'];

const none: Audit = {
  createdAt: null,
  createdBy: null,
  lastModifiedAt: null,
  lastModifiedBy: null,
};

async function show(audit: Audit | undefined) {
  await render(AuditNote, { inputs: { audit } });
}

function lines(): string[] {
  return screen.getAllByRole('paragraph').map((line) => line.textContent?.trim() ?? '');
}

describe('AuditNote', () => {
  // US-30, criteria 1 and 2: who and when, for the making and for the last touch.
  it('says who made it and when, and who touched it last and when', async () => {
    await show({
      createdAt: '2026-09-27T21:00:00Z',
      createdBy: 'euge',
      lastModifiedAt: '2026-09-28T01:30:00Z',
      lastModifiedBy: 'pablo',
    });

    const [created, modified] = lines();

    expect(created).toMatch(/^Creado por euge el .*2026/);
    expect(modified).toMatch(/^Última modificación por pablo el .*2026/);
  });

  // es-AR writes the hour as "3:12 p. m.", which already ends in a period.
  it('does not end a line with two periods after the hour', async () => {
    await show({
      ...none,
      createdAt: '2026-09-27T21:00:00Z',
      lastModifiedAt: '2026-09-27T21:00:00Z',
    });

    for (const line of lines()) expect(line).not.toMatch(/\.\.$/);
  });

  it('says it was never touched since it was made, instead of leaving the line out', async () => {
    await show({ ...none, createdAt: '2026-09-27T21:00:00Z', createdBy: 'euge' });

    expect(lines()[1]).toBe('Sin modificaciones desde que se creó');
  });

  // Something written by nobody who was signed in: the moment is worth showing
  // and no author is made up for it.
  it('leaves the author out when nobody was signed in', async () => {
    await show({ ...none, createdAt: '2026-09-27T21:00:00Z' });

    expect(lines()[0]).toMatch(/^Creado el .*2026/);
    expect(lines()[0]).not.toContain(' por ');
  });

  // US-30, criterion 4: from before the columns existed. It says so, and no
  // date appears anywhere, least of all today's.
  it('says there is no record for a row from before the audit existed', async () => {
    await show(none);

    expect(lines()).toEqual([
      'Sin registro de quién lo creó ni de cuándo',
      'Sin registro de modificaciones',
    ]);
    expect(screen.queryByText(/\d{4}/)).toBeNull();
  });

  it('says there is no record while the row itself has not arrived', async () => {
    await show(undefined);

    expect(lines()[0]).toBe('Sin registro de quién lo creó ni de cuándo');
  });
});
