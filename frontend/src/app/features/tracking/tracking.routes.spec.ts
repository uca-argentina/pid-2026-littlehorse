import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { paymentReturnUrl } from './payment-return.service';
import { trackingRoutes } from './tracking.routes';

const token = '9f3c2ba7d81e4c06a1b2c3d4e5f60718';

describe('trackingRoutes', () => {
  // Mercado Pago appends payment_id to the return address; the screen has to
  // receive it through the route, whatever the gateway calls it.
  it('hands the return screen the payment mercado pago appended', async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter(
          [{ path: ':venueSlug/orders', children: trackingRoutes }],
          withComponentInputBinding(),
        ),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    const harness = await RouterTestingHarness.create();

    await harness.navigateByUrl(
      `/bar-alfa/orders/K-4821/${token}/payment?payment_id=123456&status=approved`,
    );

    const sent = TestBed.inject(HttpTestingController).expectOne(
      paymentReturnUrl('bar-alfa', 'K-4821', token),
    );
    expect(sent.request.body).toEqual({ paymentId: '123456' });
  });
});
