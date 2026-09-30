import {
  Component,
  DestroyRef,
  InjectionToken,
  afterNextRender,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import type { ElementRef } from '@angular/core';
import { loadMercadoPago } from '@mercadopago/sdk-js';

/** How the button is drawn and what it does when tapped. */
export interface WalletSettings {
  readonly container: HTMLElement;
  readonly publicKey: string;
  /** Tapped: confirm the order and answer the checkout id to open. */
  readonly onSubmit: () => Promise<string>;
  /** Drawn for real: Mercado Pago's own placeholder is gone. */
  readonly onReady: () => void;
  readonly onError: (error: unknown) => void;
}

/** A drawn button, to be let go of when the screen leaves. */
export interface WalletButton {
  unmount(): void;
}

export type RenderWallet = (settings: WalletSettings) => Promise<WalletButton>;

/**
 * Draws Mercado Pago's own button — a token so a test draws a stand-in and
 * never loads anything from sdk.mercadopago.com.
 */
export const RENDER_WALLET = new InjectionToken<RenderWallet>('RENDER_WALLET', {
  providedIn: 'root',
  factory: () => renderMercadoPagoWallet,
});

/** The only part of MercadoPago.js this app uses. The package ships no types for it. */
type MercadoPagoConstructor = new (
  publicKey: string,
  options: { locale: string },
) => {
  bricks(): {
    create(brick: 'wallet', containerId: string, settings: object): Promise<WalletButton>;
  };
};

let nextContainer = 0;

/**
 * The size of the checkout's own button (.cta in checkout.page.scss), so that
 * switching from one to the other does not move the screen.
 */
const BUTTON_HEIGHT = '58px';
const BUTTON_CORNERS = '15px';

/**
 * How long after Mercado Pago says it is ready the button is shown even if no
 * <button> turned up: better its placeholder for a moment than a stand-in
 * nobody can tap.
 */
const REVEAL_ANYWAY_MS = 1000;

/**
 * Mercado Pago's Wallet Brick for Checkout Pro (US-24), creating the checkout
 * on submit. Verified against Mercado Pago's docs on 2026-09-30:
 * - onSubmit resolves the preference id; the brick then redirects by itself.
 * - redirectMode "self" keeps it in this tab.
 * - No purpose "wallet_purchase": it would require a Mercado Pago account, and
 *   a card without one must work too.
 * - The dark button is theme "dark". The docs' table says "black", and with
 *   "black" create() throws and nothing is drawn (tried in a browser, same day).
 */
async function renderMercadoPagoWallet(settings: WalletSettings): Promise<WalletButton> {
  await loadMercadoPago();

  const MercadoPago = (window as unknown as { MercadoPago: MercadoPagoConstructor }).MercadoPago;
  settings.container.id ||= `mp-wallet-${nextContainer++}`;

  return new MercadoPago(settings.publicKey, { locale: 'es-AR' })
    .bricks()
    .create('wallet', settings.container.id, {
      initialization: { redirectMode: 'self' },
      customization: {
        theme: 'dark',
        customStyle: {
          hideValueProp: true,
          buttonHeight: BUTTON_HEIGHT,
          borderRadius: BUTTON_CORNERS,
        },
      },
      callbacks: {
        onSubmit: settings.onSubmit,
        onReady: settings.onReady,
        onError: settings.onError,
      },
    });
}

/**
 * Mercado Pago's own "Pagar con Mercado Pago" button, with its official logo
 * (/mp-review, quality practice 7). Drawn inside this component and let go of
 * when it leaves the screen, as Mercado Pago's docs require. When the script
 * cannot draw it — an ad blocker cuts sdk.mercadopago.com — it says so, and the
 * checkout falls back to its own button.
 *
 * Mercado Pago first draws a light grey placeholder, a white flash on this dark
 * screen. It stays hidden until it says it is ready, and a stand-in shaped and
 * coloured like its dark button (#1a1a1a, measured in a browser) keeps its
 * place. Covering it alone was not enough: its corners showed around the cover.
 */
@Component({
  selector: 'drinkit-mercado-pago-wallet',
  host: {
    '[style.--button-height]': 'buttonHeight',
    '[style.--button-corners]': 'buttonCorners',
  },
  template: `
    <div
      #container
      class="wallet"
      [class.drawing]="!ready()"
      data-mp-checkout-cta="checkout-pro"
    ></div>
    @if (!ready()) {
      <div class="cover" aria-hidden="true"></div>
    }
  `,
  styles: `
    /* Whatever the brick draws inside — its placeholder is taller than its
       button — the screen around it does not move. */
    :host {
      display: block;
      position: relative;
      height: var(--button-height);
    }
    /* The brick wraps its button in a 4px vertical margin: taken back, so the
       button sits exactly where the checkout's own one was. */
    .wallet {
      display: flow-root;
      min-height: calc(var(--button-height) + 8px);
      margin: -4px 0;
    }
    .drawing {
      visibility: hidden;
    }
    .cover {
      position: absolute;
      inset: 0;
      border-radius: var(--button-corners);
      background: #1a1a1a;
    }
  `,
})
export class MercadoPagoWallet {
  readonly publicKey = input.required<string>();

  /** Confirms the order and answers the checkout id; rejected when it is refused. */
  readonly submit = input.required<() => Promise<string>>();

  /** The button could not be drawn, or broke: use the app's own. */
  readonly failed = output<void>();

  protected readonly buttonHeight = BUTTON_HEIGHT;

  protected readonly buttonCorners = BUTTON_CORNERS;

  /** Mercado Pago's placeholder is gone and its button is drawn. */
  protected readonly ready = signal(false);

  private readonly render = inject(RENDER_WALLET);

  private readonly container = viewChild.required<ElementRef<HTMLElement>>('container');

  private button: WalletButton | null = null;

  private stopWatching: () => void = () => undefined;

  private gone = false;

  constructor() {
    afterNextRender(() => void this.draw());

    inject(DestroyRef).onDestroy(() => {
      this.gone = true;
      this.stopWatching();
      this.button?.unmount();
    });
  }

  private async draw(): Promise<void> {
    try {
      const button = await this.render({
        container: this.container().nativeElement,
        publicKey: this.publicKey(),
        onSubmit: () => this.submit()(),
        onReady: () => this.revealOnceDrawn(),
        onError: () => this.failed.emit(),
      });

      // Left the screen while the script was still loading.
      if (this.gone) return button.unmount();

      this.button = button;
    } catch {
      if (!this.gone) this.failed.emit();
    }
  }

  /**
   * Mercado Pago says it is ready about ten milliseconds before its button
   * replaces its light placeholder (measured in a browser on 2026-09-30), so
   * the stand-in waits for the button itself.
   */
  private revealOnceDrawn(): void {
    const container = this.container().nativeElement;
    const reveal = () => {
      this.stopWatching();
      this.ready.set(true);
    };

    if (container.querySelector('button')) return reveal();

    const watch = new MutationObserver(() => {
      if (container.querySelector('button')) reveal();
    });
    const anyway = setTimeout(reveal, REVEAL_ANYWAY_MS);
    watch.observe(container, { childList: true, subtree: true });

    this.stopWatching = () => {
      watch.disconnect();
      clearTimeout(anyway);
    };
  }
}
