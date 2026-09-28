import { kdsHubUrl } from './kds-board-channel';

describe('kdsHubUrl', () => {
  // Development: the Angular proxy forwards /api and strips it on the way.
  it('goes through the dev proxy when there is no API address', () => {
    expect(kdsHubUrl('')).toBe('/api/hubs/kds');
  });

  // Deployed, the PWA and the API are on different hosts, and the SignalR
  // client does not go through apiBaseUrlInterceptor like HttpClient does.
  it('goes straight to the API host when the deploy gives one', () => {
    expect(kdsHubUrl('https://api.drinkit.example')).toBe('https://api.drinkit.example/hubs/kds');
  });
});
