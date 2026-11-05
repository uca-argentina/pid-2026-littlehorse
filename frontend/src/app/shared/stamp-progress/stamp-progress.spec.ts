import { render, screen } from '@testing-library/angular';
import type { StampCardView } from '../../core/stamp-card/stamp-cards';
import { StampProgress } from './stamp-progress';

const card: StampCardView = {
  isActive: true,
  stampsForReward: 5,
  rewardProductId: 'id-1',
  rewardName: 'Gin Tonic',
  stamps: 3,
  canRedeem: false,
};

async function show(view: StampCardView) {
  await render(StampProgress, { inputs: { card: view } });
}

describe('StampProgress', () => {
  it('says how many stamps there are out of how many it takes', async () => {
    await show(card);

    expect(screen.getByText('3 de 5 sellos')).toBeTruthy();
  });

  it('is a progress bar with its numbers for a screen reader', async () => {
    await show(card);

    const bar = screen.getByRole('progressbar', { name: 'Sellos de tu tarjeta' });

    expect(bar.getAttribute('aria-valuenow')).toBe('3');
    expect(bar.getAttribute('aria-valuemax')).toBe('5');
    expect(bar.getAttribute('aria-valuetext')).toBe('3 de 5 sellos');
  });

  it('names the reward it is working toward', async () => {
    await show(card);

    expect(screen.getByText(/Juntá 5 sellos y te regalamos un Gin Tonic/)).toBeTruthy();
  });

  it('draws one slot per stamp it takes, and fills the ones earned', async () => {
    await show(card);

    const slots = document.querySelectorAll('.slot');

    expect(slots).toHaveLength(5);
    expect(document.querySelectorAll('.slot-on')).toHaveLength(3);
  });

  it('says the reward is ready when the card is complete', async () => {
    await show({ ...card, stamps: 5, canRedeem: true });

    expect(screen.getByRole('status').textContent).toContain('Gin Tonic');
    expect(screen.getByText('5 de 5 sellos')).toBeTruthy();
  });

  it('draws no slots for a card that takes more than it can fit, and still says the numbers', async () => {
    await show({ ...card, stampsForReward: 50, stamps: 12 });

    expect(document.querySelectorAll('.slot')).toHaveLength(0);
    expect(screen.getByText('12 de 50 sellos')).toBeTruthy();
  });
});
