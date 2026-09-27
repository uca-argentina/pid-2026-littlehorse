import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL, apiBaseUrlInterceptor } from './api-base-url-interceptor';

describe('apiBaseUrlInterceptor', () => {
  let http: HttpClient;
  let backend: HttpTestingController;

  function configure(baseUrl: string): void {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([apiBaseUrlInterceptor])),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: baseUrl },
      ],
    });

    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
  }

  afterEach(() => backend.verify());

  // Development: the Angular proxy takes /api and strips it on the way.
  it('leaves the request alone when no base address was built in', () => {
    configure('');

    http.get('/api/staff/products').subscribe();

    backend.expectOne('/api/staff/products');
  });

  it('sends an /api request to the API host, without the prefix', () => {
    configure('https://api.example.net');

    http.get('/api/staff/products').subscribe();

    backend.expectOne('https://api.example.net/staff/products');
  });

  // A product photo is already a full address, on the storage account.
  it('leaves a request that does not start with /api alone', () => {
    configure('https://api.example.net');

    http.get('https://storage.example.net/product-images/gin.jpg').subscribe();

    backend.expectOne('https://storage.example.net/product-images/gin.jpg');
  });
});
