import { Component, computed, effect, inject, input, model, output, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzCheckboxModule } from 'ng-zorro-antd/checkbox';
import { NzIconModule } from 'ng-zorro-antd/icon';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzInputNumberModule } from 'ng-zorro-antd/input-number';
import { NzMessageService } from 'ng-zorro-antd/message';
import { NzModalModule } from 'ng-zorro-antd/modal';
import { NzSelectModule } from 'ng-zorro-antd/select';
import { NzSkeletonModule } from 'ng-zorro-antd/skeleton';
import { NzTableModule } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { AiIcon, AiSuggest } from '../../core/ai-suggest';
import { injectConfirm } from '../../core/confirm';
import { CategoryProposal, CodeScheme, CodeSegment, CodeTerm, ProductCategory } from '../../core/models';
import { ReceiptApi, errorMessage } from '../../core/receipt-api';

/** A suggested category in the list: ticked ones are created when the pattern is saved. */
interface CategoryRow extends CategoryProposal {
  pick: boolean;
}

/**
 * The questionnaire: what the business is and sells, then the naming and code pattern the AI proposes from it,
 * for the user to adjust and save. Until it is saved the AI suggestions on the product page stay off.
 */
@Component({
  selector: 'app-code-scheme',
  imports: [
    AiIcon,
    AiSuggest,
    FormsModule,
    NzButtonModule,
    NzCheckboxModule,
    NzIconModule,
    NzInputModule,
    NzInputNumberModule,
    NzModalModule,
    NzSelectModule,
    NzSkeletonModule,
    NzTableModule,
    NzTagModule,
  ],
  template: `
    <nz-modal [nzVisible]="open()" nzTitle="รูปแบบชื่อและรหัสสินค้า" nzWidth="760px" [nzFooter]="footer" (nzOnCancel)="open.set(false)">
      <ng-container *nzModalContent>
        <!-- while the AI works nothing can be changed: what it returns would overwrite it -->
        <fieldset [disabled]="busy()" [class.locked]="busy()">
        <section class="step">
          <h3>1. ร้านขายอะไร</h3>
          <p class="muted">ตอบแล้วให้ AI วางรูปแบบชื่อหมวด ชื่อสินค้า และโครงสร้างรหัสให้ ปรับแก้เองได้ก่อนบันทึก</p>
          <div class="grid gap-3">
            <label class="field">
              <span class="field-label">ประเภทธุรกิจ <span class="req">*</span></span>
              <input nz-input maxlength="200" placeholder="เช่น ร้านเฟอร์นิเจอร์" [ngModel]="businessType()" (ngModelChange)="businessType.set($event)" />
            </label>
            <div class="field">
              <span class="field-label">ประเภทสินค้าที่ขาย <span class="req">*</span></span>
              <nz-select
                class="w-full"
                nzMode="tags"
                [nzTokenSeparators]="[',']"
                nzPlaceHolder="พิมพ์แล้วกด Enter เช่น เตียง, ที่นอน, ผ้าปู"
                [ngModel]="productTypes()"
                (ngModelChange)="productTypes.set($event)"
                aria-label="ประเภทสินค้าที่ขาย"
              >
                @for (t of typeOptions(); track t) {
                  <nz-option [nzValue]="t" [nzLabel]="t"></nz-option>
                }
              </nz-select>
            </div>
            <div class="field">
              <label class="field-label" for="scheme-notes">สินค้าแต่ละชิ้นต่างกันที่อะไร</label>
              <div class="notes" [class.ai-busy]="suggestingNotes()">
                <textarea
                  id="scheme-notes"
                  nz-input
                  rows="5"
                  maxlength="1000"
                  [placeholder]="notesPlaceholder"
                  [ngModel]="notes()"
                  (ngModelChange)="notes.set($event)"
                ></textarea>
                <app-ai-suggest
                  class="notes-ai"
                  [busy]="suggestingNotes()"
                  [disabled]="!canSuggestNotes()"
                  [hint]="canSuggestNotes() || suggestingNotes() ? 'ให้ AI แนะนำจากประเภทสินค้า' : 'กรอกประเภทธุรกิจและประเภทสินค้าก่อน'"
                  (pressed)="suggestNotes()"
                />
              </div>
            </div>
            <div>
              <button nz-button nzType="primary" nzGhost [disabled]="!canPropose()" (click)="propose()">
                <app-ai-icon class="mr-1.5" [class.twinkle]="proposing()" />
                {{ segments().length ? 'สร้างรูปแบบรหัสใหม่ด้วย AI' : 'สร้างรูปแบบรหัสด้วย AI' }}
              </button>
              @if (proposing()) {
                <span class="muted ml-2 text-[12px]">AI กำลังคิด อาจใช้เวลาสักครู่…</span>
              }
            </div>
          </div>
        </section>

        @if (proposing() && !segments().length) {
          <section class="step">
            <h3>2. รูปแบบที่ใช้</h3>
            <nz-skeleton [nzActive]="true" [nzTitle]="false" [nzParagraph]="{ rows: 6 }" />
          </section>
        } @else if (segments().length) {
          <section class="step">
            <h3>2. รูปแบบที่ใช้</h3>
            <div class="grid gap-3">
              <label class="field" [class.ai-busy]="proposing()">
                <span class="field-label">รูปแบบชื่อหมวด</span>
                <input nz-input maxlength="300" placeholder="เช่น ประเภทสินค้า + ขนาด เช่น เตียงนอน 6 ฟุต" [ngModel]="categoryNameHint()" (ngModelChange)="categoryNameHint.set($event)" />
              </label>
              <label class="field" [class.ai-busy]="proposing()">
                <span class="field-label">รูปแบบชื่อสินค้า</span>
                <input nz-input maxlength="300" placeholder="เช่น แบรนด์ รุ่น สี เช่น Lotus Burgi ขาว" [ngModel]="productNameHint()" (ngModelChange)="productNameHint.set($event)" />
              </label>

              <div class="field">
                <span class="field-label">โครงสร้างรหัสสินค้า: รหัสหมวด + ส่วนต่อไปนี้ตามลำดับ</span>
                <nz-table #st nzSize="small" [class.ai-busy]="proposing()" [nzData]="segments()" [nzShowPagination]="false" [nzFrontPagination]="false">
                  <thead>
                    <tr>
                      <th nzWidth="40px">#</th>
                      <th>ส่วน</th>
                      <th nzWidth="110px">ความยาว</th>
                      <th nzWidth="130px">ชนิด</th>
                      <th nzWidth="48px"></th>
                    </tr>
                  </thead>
                  <tbody>
                    @for (s of st.data; track $index; let i = $index) {
                      <tr>
                        <td class="num muted">{{ i + 1 }}</td>
                        <td>
                          <input nz-input nzSize="small" maxlength="50" placeholder="เช่น แบรนด์" [ngModel]="s.label" (ngModelChange)="setSegment(i, { label: $event })" [attr.aria-label]="'ชื่อส่วนที่ ' + (i + 1)" />
                        </td>
                        <td>
                          <nz-input-number nzSize="small" class="w-full" [nzMin]="1" [nzMax]="6" [nzPrecision]="0" [ngModel]="s.length" (ngModelChange)="setSegment(i, { length: $event ?? 1 })" nzAddonAfter="ตัว" />
                        </td>
                        <td>
                          <nz-select nzSize="small" class="w-full" [ngModel]="s.digits" (ngModelChange)="setSegment(i, { digits: $event })">
                            <nz-option [nzValue]="true" nzLabel="ตัวเลข"></nz-option>
                            <nz-option [nzValue]="false" nzLabel="ตัวอักษร"></nz-option>
                          </nz-select>
                        </td>
                        <td>
                          <button nz-button nzType="text" nzSize="small" nzDanger [disabled]="segments().length === 1" (click)="removeSegment(i)" [attr.aria-label]="'ลบส่วน ' + s.label">
                            <nz-icon nzType="delete" />
                          </button>
                        </td>
                      </tr>
                    }
                  </tbody>
                </nz-table>
                <div class="mt-2 flex flex-wrap items-center justify-between gap-2">
                  <button nz-button nzType="dashed" nzSize="small" [disabled]="segments().length >= 6" (click)="addSegment()"><nz-icon nzType="plus" /> เพิ่มส่วน</button>
                  <span class="muted text-[12px]">หน้าตารหัส: <span class="num">{{ shape() }}</span></span>
                </div>
              </div>

              <label class="field" [class.ai-busy]="proposing()">
                <span class="field-label">คำอธิบายรหัส (แสดงตอนเพิ่มสินค้า)</span>
                <input nz-input maxlength="500" placeholder="เช่น รหัสหมวด + แบรนด์ 3 หลัก + สี 3 หลัก → BED6123111" [ngModel]="codeHint()" (ngModelChange)="codeHint.set($event)" />
              </label>

              @if (examples().length) {
                <div class="field">
                  <span class="field-label">ตัวอย่างจาก AI</span>
                  <ul class="examples">
                    @for (e of examples(); track $index) {
                      <li><span class="num">{{ e.code }}</span> {{ e.name }}</li>
                    }
                  </ul>
                </div>
              }
            </div>
          </section>
        }

        @if (newCategories().length) {
          <section class="step">
            <h3>3. หมวดที่ AI แนะนำ</h3>
            <p class="muted">ติ๊กหมวดที่จะสร้างตอนบันทึก แก้รหัสหมวดได้</p>
            <nz-table #ct nzSize="small" [class.ai-busy]="proposing()" [nzData]="newCategories()" [nzShowPagination]="false" [nzFrontPagination]="false">
              <thead>
                <tr>
                  <th nzWidth="40px" [nzChecked]="allPicked()" [nzIndeterminate]="!allPicked() && somePicked()" (nzCheckedChange)="pickAll($event)" aria-label="เลือกทั้งหมด"></th>
                  <th>ชื่อหมวด</th>
                  <th nzWidth="140px">รหัสหมวด</th>
                </tr>
              </thead>
              <tbody>
                @for (c of ct.data; track c.name; let i = $index) {
                  <tr>
                    <td [nzChecked]="c.pick" (nzCheckedChange)="setCategory(i, { pick: $event })"></td>
                    <td>{{ c.name }}</td>
                    <td>
                      <input nz-input nzSize="small" class="num uppercase" maxlength="6" [ngModel]="c.codePrefix" (ngModelChange)="setCategory(i, { codePrefix: $event.toUpperCase() })" [attr.aria-label]="'รหัสหมวด ' + c.name" />
                    </td>
                  </tr>
                }
              </tbody>
            </nz-table>
          </section>
        }

        @if (terms().length) {
          <section class="step">
            <h3>รหัสที่ระบบจำไว้ ({{ terms().length }})</h3>
            <p class="muted">จำจากสินค้าที่บันทึกด้วยรหัสที่ AI แนะนำ — สินค้าชิ้นต่อไปที่มีคำเดียวกันจะได้รหัสเดิม ลบได้ถ้าจำผิด</p>
            <nz-table #tt nzSize="small" [nzData]="terms()" [nzPageSize]="10" [nzHideOnSinglePage]="true">
              <thead>
                <tr>
                  <th nzWidth="140px">ส่วน</th>
                  <th>คำ</th>
                  <th nzWidth="100px">รหัส</th>
                  <th nzWidth="48px"></th>
                </tr>
              </thead>
              <tbody>
                @for (t of tt.data; track t.id) {
                  <tr>
                    <td class="muted">{{ segmentLabel(t.segment) }}</td>
                    <td>{{ t.term }}</td>
                    <td class="num">{{ t.code }}</td>
                    <td>
                      <button nz-button nzType="text" nzSize="small" nzDanger (click)="deleteTerm(t)" [attr.aria-label]="'ลบรหัสของ ' + t.term"><nz-icon nzType="delete" /></button>
                    </td>
                  </tr>
                }
              </tbody>
            </nz-table>
          </section>
        }
        </fieldset>
      </ng-container>
      <ng-template #footer>
        <div class="flex items-center gap-2">
          @if (scheme()) {
            <button nz-button nzDanger [disabled]="busy()" (click)="askReset()">ล้างรูปแบบ (ปิด AI)</button>
          }
          <span class="flex-1"></span>
          <button nz-button (click)="open.set(false)">ยกเลิก</button>
          <button nz-button nzType="primary" [disabled]="!canSave() || proposing() || suggestingNotes()" [nzLoading]="saving()" (click)="save()">บันทึก</button>
        </div>
      </ng-template>
    </nz-modal>
  `,
  styles: `
    fieldset { min-width: 0; margin: 0; padding: 0; border: 0; }
    fieldset.locked { pointer-events: none; }
    .twinkle { animation: twinkle 0.9s ease-in-out infinite; }
    @keyframes twinkle { 50% { opacity: 0.35; transform: scale(0.85); } }
    @media (prefers-reduced-motion: reduce) { .twinkle { animation: none; } }
    .step + .step { margin-top: 20px; padding-top: 16px; border-top: 1px solid var(--line); }
    h3 { margin: 0 0 4px; font-size: 15px; font-weight: 600; }
    p.muted { margin: 0 0 12px; font-size: 13px; }
    .field { display: block; }
    .field-label { display: block; margin-bottom: 4px; font-size: 13px; color: var(--ink-2); }
    .req { color: var(--error); }
    .notes { position: relative; }
    .notes textarea { padding-right: 34px; }
    .notes-ai { position: absolute; top: 6px; right: 8px; z-index: 3; }
    .examples { margin: 0; padding-left: 18px; font-size: 13px; }
    .examples .num { margin-right: 6px; }
  `,
})
export class CodeSchemeEditor {
  private readonly api = inject(ReceiptApi);
  private readonly message = inject(NzMessageService);
  private readonly confirm = injectConfirm();

  readonly open = model(false);
  readonly companyId = input.required<string>();
  /** The saved pattern, or null when the questionnaire hasn't been answered. */
  readonly scheme = input<CodeScheme | null>(null);
  readonly categories = input<ProductCategory[]>([]);
  /** After saving or clearing (null). Categories may have been created too. */
  readonly saved = output<CodeScheme | null>();

  protected readonly businessType = signal('');
  protected readonly productTypes = signal<string[]>([]);
  protected readonly notes = signal('');
  protected readonly categoryNameHint = signal('');
  protected readonly productNameHint = signal('');
  protected readonly codeHint = signal('');
  protected readonly segments = signal<CodeSegment[]>([]);
  protected readonly examples = signal<{ name: string; code: string }[]>([]);
  protected readonly newCategories = signal<CategoryRow[]>([]);
  protected readonly terms = signal<CodeTerm[]>([]);
  protected readonly proposing = signal(false);
  protected readonly suggestingNotes = signal(false);
  protected readonly notesPlaceholder = 'เช่น\n- เตียงนอน\nแบรนด์ รุ่น ขนาด\n- โซฟา\nแบรนด์ ขนาด สี';
  protected readonly saving = signal(false);

  /** What can be picked in the tags box: the category names already there, besides what was typed. */
  protected readonly typeOptions = computed(() => [...new Set([...this.productTypes(), ...this.categories().map((c) => c.name)])]);
  /** The AI is working or the pattern is being saved: the form is locked. */
  protected readonly busy = computed(() => this.proposing() || this.suggestingNotes() || this.saving());
  protected readonly canPropose = computed(
    () => !!this.businessType().trim() && this.productTypes().length > 0 && !this.busy(),
  );
  protected readonly canSuggestNotes = computed(
    () => !!this.businessType().trim() && this.productTypes().length > 0 && !this.busy(),
  );
  protected readonly canSave = computed(
    () =>
      !!this.businessType().trim() &&
      this.productTypes().length > 0 &&
      this.segments().length > 0 &&
      this.segments().every((s) => s.label.trim() && s.length >= 1) &&
      this.newCategories().every((c) => !c.pick || /^[A-Z0-9]{1,6}$/.test(c.codePrefix)),
  );
  protected readonly shape = computed(
    () => 'หมวด + ' + this.segments().map((s) => `${s.label || '?'}(${(s.digits ? '0' : 'A').repeat(s.length)})`).join(' + '),
  );
  protected readonly allPicked = computed(() => this.newCategories().every((c) => c.pick));
  protected readonly somePicked = computed(() => this.newCategories().some((c) => c.pick));

  constructor() {
    // Each time it opens, start from what is saved.
    effect(() => {
      if (!this.open()) return;
      untracked(() => this.reset(this.scheme()));
    });
  }

  private reset(s: CodeScheme | null) {
    this.businessType.set(s?.businessType ?? '');
    this.productTypes.set(s?.productTypes ?? this.categories().map((c) => c.name));
    this.notes.set(s?.notes ?? '');
    this.categoryNameHint.set(s?.categoryNameHint ?? '');
    this.productNameHint.set(s?.productNameHint ?? '');
    this.codeHint.set(s?.codeHint ?? '');
    this.segments.set(s?.segments.map((x) => ({ ...x })) ?? []);
    this.terms.set(s?.terms ?? []);
    this.examples.set([]);
    this.newCategories.set([]);
  }

  protected segmentLabel(key: string): string {
    return this.segments().find((s) => s.key === key)?.label ?? key;
  }

  protected propose() {
    if (!this.canPropose()) return;
    this.proposing.set(true);
    this.api
      .proposeCodeScheme(this.companyId(), {
        businessType: this.businessType().trim(),
        productTypes: this.productTypes(),
        notes: this.notes().trim() || null,
      })
      .subscribe({
        next: (p) => {
          this.proposing.set(false);
          this.categoryNameHint.set(p.categoryNameHint ?? '');
          this.productNameHint.set(p.productNameHint ?? '');
          this.codeHint.set(p.codeHint ?? '');
          this.segments.set(p.segments);
          this.examples.set(p.examples);
          this.newCategories.set(p.categories.filter((c) => !c.exists).map((c) => ({ ...c, pick: true })));
        },
        error: (e) => {
          this.proposing.set(false);
          this.message.error(errorMessage(e));
        },
      });
  }

  /** Fills "how products differ" per product type; what was typed there is given to the AI to follow. */
  protected suggestNotes() {
    if (!this.canSuggestNotes()) return;
    this.suggestingNotes.set(true);
    this.api
      .suggestCodeNotes(this.companyId(), {
        businessType: this.businessType().trim(),
        productTypes: this.productTypes(),
        notes: this.notes().trim() || null,
      })
      .subscribe({
        next: (r) => {
          this.suggestingNotes.set(false);
          this.notes.set(r.notes);
        },
        error: (e) => {
          this.suggestingNotes.set(false);
          this.message.error(errorMessage(e));
        },
      });
  }

  protected setSegment(index: number, change: Partial<CodeSegment>) {
    this.segments.update((list) => list.map((s, i) => (i === index ? { ...s, ...change } : s)));
  }

  protected addSegment() {
    this.segments.update((list) => [...list, { key: '', label: '', length: 3, digits: true }]);
  }

  protected removeSegment(index: number) {
    this.segments.update((list) => list.filter((_, i) => i !== index));
  }

  protected setCategory(index: number, change: Partial<CategoryRow>) {
    this.newCategories.update((list) => list.map((c, i) => (i === index ? { ...c, ...change } : c)));
  }

  protected pickAll(on: boolean) {
    this.newCategories.update((list) => list.map((c) => ({ ...c, pick: on })));
  }

  protected save() {
    if (!this.canSave() || this.saving()) return;
    const keys = new Set(this.segments().map((s) => s.key));
    const lost = this.terms().filter((t) => !keys.has(t.segment)).length;
    const go = () => {
      this.saving.set(true);
      this.api
        .saveCodeScheme(this.companyId(), {
          businessType: this.businessType().trim(),
          productTypes: this.productTypes(),
          notes: this.notes().trim() || null,
          categoryNameHint: this.categoryNameHint().trim() || null,
          productNameHint: this.productNameHint().trim() || null,
          codeHint: this.codeHint().trim() || null,
          segments: this.segments().map((s) => ({ ...s, label: s.label.trim() })),
          newCategories: this.newCategories()
            .filter((c) => c.pick)
            .map((c) => ({ name: c.name, codePrefix: c.codePrefix })),
        })
        .subscribe({
          next: (s) => {
            this.saving.set(false);
            this.open.set(false);
            this.message.success('บันทึกรูปแบบแล้ว — ใช้ปุ่ม AI แนะนำ ตอนเพิ่มหมวดและสินค้าได้เลย');
            this.saved.emit(s);
          },
          error: (e) => {
            this.saving.set(false);
            this.message.error(errorMessage(e));
          },
        });
    };
    if (!lost) return go();
    this.confirm({
      title: 'ลบรหัสที่จำไว้ด้วย?',
      content: `ส่วนของรหัสที่เอาออกมีรหัสที่ระบบจำไว้ ${lost} รายการ จะถูกลบไปด้วย`,
      okText: 'บันทึก',
      onOk: go,
    });
  }

  protected deleteTerm(t: CodeTerm) {
    this.api.deleteCodeTerm(this.companyId(), t.id).subscribe({
      next: () => this.terms.update((list) => list.filter((x) => x.id !== t.id)),
      error: (e) => this.message.error(errorMessage(e)),
    });
  }

  protected askReset() {
    this.confirm({
      title: 'ล้างรูปแบบรหัส?',
      content: 'คำตอบแบบสอบถามและรหัสที่ระบบจำไว้จะถูกลบ ปุ่ม AI แนะนำจะปิดจนกว่าจะตอบแบบสอบถามใหม่ · รหัสสินค้าที่มีอยู่ไม่เปลี่ยน',
      okText: 'ล้าง',
      danger: true,
      onOk: () =>
        this.api.deleteCodeScheme(this.companyId()).subscribe({
          next: () => {
            this.open.set(false);
            this.message.success('ล้างรูปแบบแล้ว ปิด AI แนะนำรหัส');
            this.saved.emit(null);
          },
          error: (e) => this.message.error(errorMessage(e)),
        }),
    });
  }
}
