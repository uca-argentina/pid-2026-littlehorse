import {
  Component,
  DestroyRef,
  InjectionToken,
  afterNextRender,
  inject,
  output,
  signal,
  viewChild,
} from '@angular/core';
import type { ElementRef } from '@angular/core';
import { LOAD_QR_DETECTOR } from '../qr-reader';
import type { QrDetector } from '../qr-reader';

/** Opens the camera that faces away from whoever holds the tablet. */
export const OPEN_CAMERA = new InjectionToken<() => Promise<MediaStream>>('OPEN_CAMERA', {
  providedIn: 'root',
  factory: () => () =>
    // Absent outside HTTPS and on a tablet without one: the same as no camera.
    navigator.mediaDevices?.getUserMedia({ video: { facingMode: 'environment' }, audio: false }) ??
    Promise.reject(new DOMException('No camera API.', 'NotFoundError')),
});

/**
 * How often a frame is looked at. Four a second finds a QR held up for a
 * moment without keeping a tablet's processor busy all night.
 */
export const CAMERA_FRAME_MS = new InjectionToken<number>('CAMERA_FRAME_MS', {
  providedIn: 'root',
  factory: () => 250,
});

type CameraState = 'starting' | 'reading' | 'denied' | 'missing' | 'failed';

/**
 * The scan screen's camera (US-20). It only reads: what to do with a code is
 * the screen's business, and it reaches this component's parent the same way
 * a code typed by a reader does.
 */
@Component({
  selector: 'drinkit-kds-camera',
  styleUrl: './kds-camera.scss',
  templateUrl: './kds-camera.html',
})
export class KdsCamera {
  /** Every code seen, as often as it is seen: telling a repeat apart is the store's job. */
  readonly read = output<string>();

  private readonly openCamera = inject(OPEN_CAMERA);

  private readonly loadDetector = inject(LOAD_QR_DETECTOR);

  private readonly frameMs = inject(CAMERA_FRAME_MS);

  private readonly video = viewChild.required<ElementRef<HTMLVideoElement>>('video');

  protected readonly state = signal<CameraState>('starting');

  private stream: MediaStream | null = null;

  private timer: ReturnType<typeof setTimeout> | null = null;

  private closed = false;

  constructor() {
    afterNextRender(() => void this.start());

    inject(DestroyRef).onDestroy(() => {
      this.closed = true;
      if (this.timer !== null) clearTimeout(this.timer);
      this.letGo();
    });
  }

  private async start(): Promise<void> {
    try {
      const stream = await this.openCamera();

      this.stream = stream;

      // Closed while the tablet was still asking for permission.
      if (this.closed) return this.letGo();

      const video = this.video().nativeElement;
      video.srcObject = stream;
      await video.play();

      const detector = await this.loadDetector();

      this.state.set('reading');
      this.lookAgainSoon(detector);
    } catch (error: unknown) {
      this.state.set(stateFor(error));
      this.letGo();
    }
  }

  /** One frame at a time: the next is looked at only after this one is read. */
  private lookAgainSoon(detector: QrDetector): void {
    if (this.closed) return;

    this.timer = setTimeout(async () => {
      try {
        const codes = await detector.detect(this.video().nativeElement);

        for (const code of codes) this.read.emit(code);
      } catch {
        // A frame the reader could not take — the video was still starting,
        // or the tablet was busy. The next one will do.
      }

      this.lookAgainSoon(detector);
    }, this.frameMs);
  }

  private letGo(): void {
    for (const track of this.stream?.getTracks() ?? []) track.stop();

    this.stream = null;
  }
}

function stateFor(error: unknown): CameraState {
  if (!(error instanceof DOMException)) return 'failed';
  if (error.name === 'NotAllowedError' || error.name === 'SecurityError') return 'denied';
  if (error.name === 'NotFoundError' || error.name === 'OverconstrainedError') return 'missing';

  return 'failed';
}
