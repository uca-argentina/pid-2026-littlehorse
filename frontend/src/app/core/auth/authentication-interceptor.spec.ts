import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ProblemTypes } from '../api/problem-types';
import { anonymously } from './anonymous-request';
import { authenticationInterceptor } from './authentication-interceptor';
import { SessionStorage } from './session-storage';
import type { StaffSession } from './staff-session';

const aSession: StaffSession = {
  token: 'un-token',
  expiresAt: new Date(Date.now() + 60_000).toISOString(),
  username: 'euge',
  role: 'Administrator',
};

describe('authenticationInterceptor', () => {
  let http: HttpClient;
  let backend: HttpTestingController;
  let sessions: SessionStorage;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authenticationInterceptor])),
        provideHttpClientTesting(),
      ],
    });

    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
    sessions = TestBed.inject(SessionStorage);
  });

  afterEach(() => backend.verify());

  it('carries the token when there is a session', () => {
    sessions.remember(aSession);

    http.get('/api/algo').subscribe();

    expect(backend.expectOne('/api/algo').request.headers.get('Authorization')).toBe(
      'Bearer un-token',
    );
  });

  it('sends no token when the request must stay anonymous', () => {
    sessions.remember(aSession);

    http.post('/api/bar-beta/auth/login', {}, { context: anonymously() }).subscribe();

    const sent = backend.expectOne('/api/bar-beta/auth/login');

    expect(sent.request.headers.has('Authorization')).toBe(false);
  });

  it('drops the session when the token expired', () => {
    sessions.remember(aSession);

    http.get('/api/algo').subscribe({ error: () => undefined });
    backend
      .expectOne('/api/algo')
      .flush({ type: ProblemTypes.sessionExpired }, { status: 401, statusText: 'Unauthorized' });

    expect(sessions.hasSession()).toBe(false);
    expect(sessions.expired()).toBe(true);
  });

  it('drops the session when the API asks for authentication', () => {
    sessions.remember(aSession);

    http.get('/api/algo').subscribe({ error: () => undefined });
    backend
      .expectOne('/api/algo')
      .flush(
        { type: ProblemTypes.authenticationRequired },
        { status: 401, statusText: 'Unauthorized' },
      );

    expect(sessions.hasSession()).toBe(false);
  });

  it('keeps the session alone when credentials are wrong', () => {
    sessions.remember(aSession);

    http.get('/api/algo').subscribe({ error: () => undefined });
    backend
      .expectOne('/api/algo')
      .flush(
        { type: ProblemTypes.invalidCredentials },
        { status: 401, statusText: 'Unauthorized' },
      );

    expect(sessions.hasSession()).toBe(true);
  });
});
