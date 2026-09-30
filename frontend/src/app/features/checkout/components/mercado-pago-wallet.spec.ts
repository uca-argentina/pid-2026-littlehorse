import { render } from '@testing-library/angular';
import { RENDER_WALLET, MercadoPagoWallet } from './mercado-pago-wallet';
import type { WalletSettings } from './mercado-pago-wallet';

/** Stands in for Mercado Pago's script: remembers how it was asked to draw the button. */
function aRenderer(fails = false) {
  const drawn: WalletSettings[] = [];
  const unmount = vi.fn();
  const renderer = vi.fn((settings: WalletSettings) => {
    drawn.push(settings);

    return fails ? Promise.reject(new Error('blocked')) : Promise.resolve({ unmount });
  });

  return { renderer, drawn, unmount };
}

async function drawWallet(renderer: (settings: WalletSettings) => Promise<{ unmount(): void }>) {
  const failed = vi.fn();
  const submit = vi.fn(() => Promise.resolve('3727754810-123'));

  const rendered = await render(MercadoPagoWallet, {
    inputs: { publicKey: 'APP_USR-public', submit },
    on: { failed },
    providers: [{ provide: RENDER_WALLET, useValue: renderer }],
  });
  await rendered.fixture.whenStable();

  return { rendered, failed, submit };
}

describe('MercadoPagoWallet', () => {
  it('draws mercado pagos button with the public key, inside itself', async () => {
    const { renderer, drawn } = aRenderer();
    const { rendered } = await drawWallet(renderer);

    expect(drawn[0]!.publicKey).toBe('APP_USR-public');
    expect(rendered.fixture.nativeElement.contains(drawn[0]!.container)).toBe(true);
  });

  // Tapped: the order is confirmed, and the button opens the checkout it answers.
  it('hands the button the checkout id of the order it confirms', async () => {
    const { renderer, drawn } = aRenderer();
    const { submit } = await drawWallet(renderer);

    await expect(drawn[0]!.onSubmit()).resolves.toBe('3727754810-123');
    expect(submit).toHaveBeenCalled();
  });

  // An ad blocker cuts sdk.mercadopago.com: the screen falls back to its own button.
  it('says so when mercado pagos script cannot draw the button', async () => {
    const { renderer } = aRenderer(true);
    const { failed } = await drawWallet(renderer);

    expect(failed).toHaveBeenCalled();
  });

  it('says so when the button reports an error of its own', async () => {
    const { renderer, drawn } = aRenderer();
    const { failed } = await drawWallet(renderer);

    drawn[0]!.onError(new Error('brick error'));

    expect(failed).toHaveBeenCalled();
  });

  // Mercado Pago draws a light grey placeholder first, a white flash on a dark
  // screen: hidden, not just covered, or its corners show around the stand-in.
  it('hides the button behind a stand-in while mercado pago is still drawing it', async () => {
    const { renderer, drawn } = aRenderer();
    const { rendered } = await drawWallet(renderer);

    expect(drawn[0]!.container.classList).toContain('drawing');
    expect(rendered.fixture.nativeElement.querySelector('.cover')).not.toBeNull();
  });

  it('uncovers the button once mercado pago has drawn it', async () => {
    const { renderer, drawn } = aRenderer();
    const { rendered } = await drawWallet(renderer);

    drawn[0]!.container.append(document.createElement('button'));
    drawn[0]!.onReady();
    await rendered.fixture.whenStable();

    expect(drawn[0]!.container.classList).not.toContain('drawing');
    expect(rendered.fixture.nativeElement.querySelector('.cover')).toBeNull();
  });

  // Tried in a browser on 2026-09-30: Mercado Pago says it is ready ten
  // milliseconds before its button replaces its light placeholder.
  it('stays covered while mercado pagos placeholder is still there', async () => {
    const { renderer, drawn } = aRenderer();
    const { rendered } = await drawWallet(renderer);

    drawn[0]!.onReady();
    await rendered.fixture.whenStable();
    expect(drawn[0]!.container.classList).toContain('drawing');

    drawn[0]!.container.append(document.createElement('button'));
    await vi.waitFor(() => expect(drawn[0]!.container.classList).not.toContain('drawing'));
  });

  // Should Mercado Pago ever draw it without a <button>, nobody is left
  // looking at a stand-in they cannot tap.
  it('uncovers it anyway a second after mercado pago says it is ready', async () => {
    const { renderer, drawn } = aRenderer();
    const { rendered } = await drawWallet(renderer);
    vi.useFakeTimers();

    try {
      drawn[0]!.onReady();
      vi.advanceTimersByTime(1000);
      rendered.fixture.detectChanges();

      expect(drawn[0]!.container.classList).not.toContain('drawing');
    } finally {
      vi.useRealTimers();
    }
  });

  // Mercado Pago's docs: a brick left mounted when the screen goes has to be unmounted.
  it('lets go of the button when it leaves the screen', async () => {
    const { renderer, unmount } = aRenderer();
    const { rendered } = await drawWallet(renderer);

    rendered.fixture.destroy();

    expect(unmount).toHaveBeenCalled();
  });
});
