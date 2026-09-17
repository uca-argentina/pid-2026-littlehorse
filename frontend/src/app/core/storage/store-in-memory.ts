import { BrowserStore } from './browser-store';

/**
 * What the browser would remember, as a Map. The runner is Node and there is
 * no localStorage there, so a spec that reached for one would fail for a
 * reason that has nothing to do with what it is testing. Test-only: nothing
 * outside a spec file imports this.
 */
export class StoreInMemory extends BrowserStore {
  readonly entries = new Map<string, string>();

  override read(key: string): string | null {
    return this.entries.get(key) ?? null;
  }

  override write(key: string, value: string): void {
    this.entries.set(key, value);
  }
}
