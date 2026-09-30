import { ZXING_WASM_VERSION as bundledByTheDetector } from 'barcode-detector/ponyfill';
import { ZXING_WASM_VERSION as copiedToTheAssets } from 'zxing-wasm/reader';
import { ZXING_ASSETS_PATH, locateZxingFile } from './qr-reader';

describe('locateZxingFile', () => {
  // Served by us and not by a CDN, decided on 2026-09-29: a tablet behind a
  // venue's filtered wifi must still read with its camera.
  it('finds the reader wasm among our own assets', () => {
    expect(locateZxingFile('zxing_reader.wasm', 'https://cdn.example/')).toBe(
      `${ZXING_ASSETS_PATH}zxing_reader.wasm`,
    );
  });

  it('leaves anything else where the library would look', () => {
    expect(locateZxingFile('something.js', 'https://cdn.example/')).toBe(
      'https://cdn.example/something.js',
    );
  });
});

describe('zxing-wasm', () => {
  /**
   * angular.json copies the wasm from the zxing-wasm in package.json, and the
   * detector runs the glue code of the zxing-wasm it bundles. Two versions
   * apart, the glue loads a wasm it was not built for. This is what fails when
   * one of the two is updated without the other.
   */
  it('is the same version the detector was built against', () => {
    expect(copiedToTheAssets).toBe(bundledByTheDetector);
  });
});
