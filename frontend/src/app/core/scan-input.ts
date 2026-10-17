import { Component, ElementRef, OnDestroy, input, model, output, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import type { IScannerControls } from '@zxing/browser';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzIconModule } from 'ng-zorro-antd/icon';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzModalModule } from 'ng-zorro-antd/modal';

/**
 * A search box that also takes barcodes. A USB/Bluetooth scanner types the code and presses Enter, so it needs
 * nothing special; the camera button reads a label with the phone or webcam (https or localhost only).
 * The server understands a scan typed while the keyboard was in Thai, so the raw text is passed on as it is.
 */
@Component({
  selector: 'app-scan-input',
  imports: [FormsModule, NzButtonModule, NzIconModule, NzInputModule, NzModalModule],
  template: `
    <nz-input-group [nzPrefix]="prefix" [nzSuffix]="suffix">
      <input
        nz-input
        autocomplete="off"
        [placeholder]="placeholder()"
        [attr.aria-label]="placeholder()"
        [ngModel]="value()"
        (ngModelChange)="value.set($event)"
        (keydown.enter)="submit()"
      />
    </nz-input-group>
    <ng-template #prefix><nz-icon nzType="barcode" class="muted" /></ng-template>
    <ng-template #suffix>
      <button
        type="button"
        class="cam"
        (click)="cameraOpen.set(true)"
        aria-label="สแกนด้วยกล้อง"
        title="สแกนด้วยกล้อง"
      >
        <nz-icon nzType="camera" />
      </button>
    </ng-template>

    <nz-modal
      [nzVisible]="cameraOpen()"
      nzTitle="สแกนบาร์โค้ด"
      [nzFooter]="null"
      (nzAfterOpen)="startCamera()"
      (nzOnCancel)="closeCamera()"
    >
      <ng-container *nzModalContent>
        @if (cameraError(); as err) {
          <p class="danger m-0">{{ err }}</p>
        } @else {
          <video #video class="block aspect-[4/3] w-full rounded-md bg-black object-cover" playsinline muted></video>
          <p class="muted mt-2 mb-0">หันกล้องไปที่บาร์โค้ดบนป้ายสินค้า ให้แท่งบาร์โค้ดอยู่แนวนอนเต็มกรอบ</p>
        }
      </ng-container>
    </nz-modal>
  `,
  styles: `
    :host { display: block; }
    .cam {
      display: grid;
      place-items: center;
      padding: 0 2px;
      border: 0;
      background: transparent;
      color: var(--ink-3);
      cursor: pointer;
    }
    .cam:hover { color: var(--accent); }
    .danger { color: var(--error); }
  `,
})
export class ScanInput implements OnDestroy {
  readonly placeholder = input('ค้นหาชื่อ หรือสแกน/พิมพ์รหัสแล้วกด Enter');
  /** The text in the box; bind it to filter a list while typing. */
  readonly value = model('');
  /** Enter in the box (a hardware scanner ends with Enter) or a camera read. */
  readonly scanned = output<string>();

  protected readonly cameraOpen = signal(false);
  protected readonly cameraError = signal<string | null>(null);
  private readonly video = viewChild<ElementRef<HTMLVideoElement>>('video');
  private controls: IScannerControls | null = null;

  protected submit() {
    const text = this.value().trim();
    if (text) this.scanned.emit(text);
  }

  protected async startCamera() {
    this.cameraError.set(null);
    if (!window.isSecureContext || !navigator.mediaDevices?.getUserMedia) {
      this.cameraError.set('กล้องใช้ได้เฉพาะเมื่อเปิดเว็บผ่าน https (หรือ localhost) — ใช้เครื่องสแกนหรือพิมพ์รหัสแทน');
      return;
    }
    const video = this.video()?.nativeElement;
    if (!video) return;
    try {
      // Loaded on first use: the decoder is only needed by people who scan with a camera.
      const { BrowserMultiFormatOneDReader } = await import('@zxing/browser');
      const reader = new BrowserMultiFormatOneDReader();
      this.controls = await reader.decodeFromConstraints(
        { video: { facingMode: 'environment' } },
        video,
        (result, _err, controls) => {
          if (!result) return;
          controls.stop();
          this.controls = null;
          this.cameraOpen.set(false);
          const text = result.getText();
          this.value.set(text);
          this.scanned.emit(text);
        },
      );
      // Closed while the camera was still starting.
      if (!this.cameraOpen()) this.stop();
    } catch (e) {
      const name = (e as { name?: string })?.name;
      this.cameraError.set(
        name === 'NotAllowedError'
          ? 'ไม่ได้รับอนุญาตให้ใช้กล้อง — อนุญาตในการตั้งค่าเบราว์เซอร์แล้วลองใหม่'
          : name === 'NotFoundError'
            ? 'ไม่พบกล้องบนเครื่องนี้'
            : 'เปิดกล้องไม่ได้',
      );
    }
  }

  protected closeCamera() {
    this.stop();
    this.cameraOpen.set(false);
  }

  ngOnDestroy() {
    this.stop();
  }

  private stop() {
    this.controls?.stop();
    this.controls = null;
  }
}
