import { fireEvent, render, screen } from '@testing-library/angular';
import { CrewPicker } from './crew-picker';
import type { CrewAccount } from './crew-picker';

const bars: CrewAccount[] = [
  { id: 'id-main', username: 'main-bar' },
  { id: 'id-vip', username: 'vip-bar' },
  { id: 'id-patio', username: 'patio-bar' },
  { id: 'id-roof', username: 'roof-bar' },
];

async function openPicker(chosen: string[] = [], accounts = bars) {
  const toggled = vi.fn();
  const rendered = await render(CrewPicker, {
    inputs: { title: 'KDS', accounts, chosen: new Set(chosen), whenNone: 'No hay cuentas de KDS.' },
    on: { toggled },
  });

  return { rendered, toggled };
}

function toggle(): HTMLButtonElement {
  return screen.getByRole('button', { name: /KDS/ }) as HTMLButtonElement;
}

describe('CrewPicker', () => {
  it('starts closed, saying nobody is chosen', async () => {
    await openPicker();

    expect(toggle().getAttribute('aria-expanded')).toBe('false');
    expect(toggle().textContent).toContain('Nadie elegido');
    expect(screen.queryByRole('checkbox')).toBeNull();
  });

  it('opens to offer every account of the role', async () => {
    const { rendered } = await openPicker();

    toggle().click();
    await rendered.fixture.whenStable();

    expect(toggle().getAttribute('aria-expanded')).toBe('true');
    expect(screen.getAllByRole('checkbox')).toHaveLength(4);
  });

  it('says which account was ticked, without choosing anything itself', async () => {
    const { rendered, toggled } = await openPicker();

    toggle().click();
    await rendered.fixture.whenStable();
    screen.getByRole('checkbox', { name: 'vip-bar' }).click();

    expect(toggled).toHaveBeenCalledWith('id-vip');
  });

  // Closed, the button is all there is: it has to say who is in.
  it('names who is chosen while closed', async () => {
    await openPicker(['id-main', 'id-vip']);

    expect(toggle().textContent).toContain('main-bar, vip-bar');
  });

  it('shortens a long choice to the first two and how many more', async () => {
    await openPicker(['id-main', 'id-vip', 'id-patio', 'id-roof']);

    expect(toggle().textContent).toContain('main-bar, vip-bar y 2 más');
  });

  it('closes with Escape, as a dropdown should', async () => {
    const { rendered } = await openPicker();

    toggle().click();
    await rendered.fixture.whenStable();
    fireEvent.keyDown(screen.getByRole('checkbox', { name: 'main-bar' }), { key: 'Escape' });
    await rendered.fixture.whenStable();

    expect(toggle().getAttribute('aria-expanded')).toBe('false');
  });

  // A venue with many accounts of one role: finding one by scrolling is slow.
  it('narrows a long list to what was typed', async () => {
    const many = Array.from({ length: 8 }, (_, i) => ({ id: `id-${i}`, username: `bar-${i}` }));
    const { rendered } = await openPicker([], many);

    toggle().click();
    await rendered.fixture.whenStable();
    fireEvent.input(screen.getByRole('searchbox'), { target: { value: 'bar-3' } });
    await rendered.fixture.whenStable();

    expect(screen.getAllByRole('checkbox').map((box) => box.getAttribute('value'))).toEqual([
      'id-3',
    ]);
  });

  it('says so when the role has no accounts at all', async () => {
    const { rendered } = await openPicker([], []);

    toggle().click();
    await rendered.fixture.whenStable();

    expect(screen.getByText('No hay cuentas de KDS.')).not.toBeNull();
  });
});
