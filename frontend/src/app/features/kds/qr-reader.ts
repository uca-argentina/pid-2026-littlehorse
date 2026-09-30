import { InjectionToken } from '@angular/core';
import type { BarcodeDetector } from 'barcode-detector/ponyfill';

/** Where angular.json copies the reader's wasm to. */
export const ZXING_ASSETS_PATH = '/zxing/';

/**
 * Where the WebAssembly reader looks for its files: the wasm among our own
 * assets, not on the CDN it would otherwise fetch it from (decided on
 * 2026-09-29), so a venue's filtered wifi cannot leave the camera blind.
 */
export function locateZxingFile(path: string, prefix: string): string {
  return path.endsWith('.wasm') ? `${ZXING_ASSETS_PATH}${path}` : prefix + path;
}

/** What the scan screen needs from a QR reader: the texts it sees in a frame. */
export interface QrDetector {
  detect(frame: HTMLVideoElement): Promise<readonly string[]>;
}

export type LoadQrDetector = () => Promise<QrDetector>;

/**
 * How the scan screen gets its reader. A token so a test can hand it one that
 * reads whatever the test says, without a camera.
 */
export const LOAD_QR_DETECTOR = new InjectionToken<LoadQrDetector>('LOAD_QR_DETECTOR', {
  providedIn: 'root',
  factory: () => loadQrDetector,
});

/**
 * The browser's own reader where it has one that reads QR — Chrome on Android,
 * which is what the bar's tablet runs — and ZXing compiled to WebAssembly
 * everywhere else: Safari, all of iOS, Firefox. Both speak the same standard
 * API, and the WebAssembly one is only downloaded where it is needed.
 */
async function loadQrDetector(): Promise<QrDetector> {
  const native = (globalThis as { BarcodeDetector?: typeof BarcodeDetector }).BarcodeDetector;

  // Chrome on a Linux desktop has the API and reads nothing with it.
  if (native !== undefined && (await native.getSupportedFormats()).includes('qr_code'))
    return readingWith(new native({ formats: ['qr_code'] }));

  const ponyfill = await import('barcode-detector/ponyfill');

  ponyfill.prepareZXingModule({ overrides: { locateFile: locateZxingFile } });

  return readingWith(new ponyfill.BarcodeDetector({ formats: ['qr_code'] }));
}

function readingWith(detector: BarcodeDetector): QrDetector {
  return {
    detect: async (frame) => (await detector.detect(frame)).map((found) => found.rawValue),
  };
}
