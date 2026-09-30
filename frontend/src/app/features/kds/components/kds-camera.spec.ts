import { render, screen } from '@testing-library/angular';
import { LOAD_QR_DETECTOR } from '../qr-reader';
import type { QrDetector } from '../qr-reader';
import { CAMERA_FRAME_MS, OPEN_CAMERA, KdsCamera } from './kds-camera';

const token = '9f3c2ba7d81e4c06a1b2c3d4e5f60718';

/** A stream with one track, enough to see whether the camera was let go of. */
function aStream(): { stream: MediaStream; stop: ReturnType<typeof vi.fn> } {
  const stop = vi.fn();
  const track = { stop } as unknown as MediaStreamTrack;

  return { stream: { getTracks: () => [track] } as unknown as MediaStream, stop };
}

/** A reader that sees whatever the test puts in front of it. */
function aDetector(sees: () => readonly string[]): QrDetector {
  return { detect: () => Promise.resolve(sees()) };
}

async function openCamera(
  open: () => Promise<MediaStream>,
  sees: () => readonly string[] = () => [],
) {
  const read = vi.fn();

  const rendered = await render(KdsCamera, {
    on: { read },
    providers: [
      { provide: OPEN_CAMERA, useValue: open },
      { provide: LOAD_QR_DETECTOR, useValue: () => Promise.resolve(aDetector(sees)) },
      { provide: CAMERA_FRAME_MS, useValue: 100 },
    ],
  });

  return { rendered, read };
}

describe('KdsCamera', () => {
  beforeEach(() => {
    // jsdom has no media pipeline: playing is taken as done.
    vi.spyOn(HTMLMediaElement.prototype, 'play').mockResolvedValue(undefined);
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it('hands over what it reads in front of it', async () => {
    vi.useFakeTimers();
    const { stream } = aStream();
    const { read } = await openCamera(
      () => Promise.resolve(stream),
      () => [token],
    );

    await vi.advanceTimersByTimeAsync(100);

    expect(read).toHaveBeenCalledWith(token);
  });

  it('hands over nothing while nothing is in front of it', async () => {
    vi.useFakeTimers();
    const { stream } = aStream();
    const { read } = await openCamera(() => Promise.resolve(stream));

    await vi.advanceTimersByTimeAsync(300);

    expect(read).not.toHaveBeenCalled();
  });

  // The tablet asked once and somebody said no. The reader and the manual
  // search still work, and the screen has to say so rather than stay black.
  it('says so when it has no permission to use the camera', async () => {
    const { rendered } = await openCamera(() =>
      Promise.reject(new DOMException('denied', 'NotAllowedError')),
    );
    await rendered.fixture.whenStable();

    expect(screen.getByRole('status').textContent).toMatch(/permiso/i);
  });

  it('says so when the tablet has no camera', async () => {
    const { rendered } = await openCamera(() =>
      Promise.reject(new DOMException('none', 'NotFoundError')),
    );
    await rendered.fixture.whenStable();

    expect(screen.getByRole('status').textContent).toMatch(/no hay c[áa]mara/i);
  });

  // A camera left on keeps its light on and the battery going for nothing.
  it('lets go of the camera when the screen closes', async () => {
    const { stream, stop } = aStream();
    const { rendered } = await openCamera(() => Promise.resolve(stream));
    await rendered.fixture.whenStable();

    rendered.fixture.destroy();

    expect(stop).toHaveBeenCalled();
  });
});
