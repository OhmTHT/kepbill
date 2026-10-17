import { DecimalPipe } from '@angular/common';
import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AbstractControl, FormBuilder, FormGroup, FormsModule, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { NzAutocompleteModule } from 'ng-zorro-antd/auto-complete';
import { NzButtonModule } from 'ng-zorro-antd/button';
import { NzCardModule } from 'ng-zorro-antd/card';
import { NzCheckboxModule } from 'ng-zorro-antd/checkbox';
import { NzDatePickerModule } from 'ng-zorro-antd/date-picker';
import { NzEmptyModule } from 'ng-zorro-antd/empty';
import { NzFormModule } from 'ng-zorro-antd/form';
import { NzGridModule } from 'ng-zorro-antd/grid';
import { NzIconModule } from 'ng-zorro-antd/icon';
import { NzInputModule } from 'ng-zorro-antd/input';
import { NzInputNumberModule } from 'ng-zorro-antd/input-number';
import { NzMessageService } from 'ng-zorro-antd/message';
import { NzModalModule } from 'ng-zorro-antd/modal';
import { NzRadioModule } from 'ng-zorro-antd/radio';
import { NzSelectModule } from 'ng-zorro-antd/select';
import { NzSwitchModule } from 'ng-zorro-antd/switch';
import { NzTableModule } from 'ng-zorro-antd/table';
import { NzTagModule } from 'ng-zorro-antd/tag';
import { NzTooltipModule } from 'ng-zorro-antd/tooltip';
import { forkJoin } from 'rxjs';
import { CdkDrag, CdkDragDrop, CdkDragHandle, CdkDropList, moveItemInArray } from '@angular/cdk/drag-drop';
import { Auth } from '../../core/auth';
import { LABEL_TEMPLATES, printLabels } from '../../core/barcode-labels';
import { CompanyStore } from '../../core/company-store';
import { injectConfirm } from '../../core/confirm';
import { CodeScheme, Product, ProductCategory, ProductCodeSuggestion, ProductDetail, Warehouse, fromDate, toDate } from '../../core/models';
import { numberFormatter } from '../../core/number-format';
import { ReceiptApi, errorMessage, stockLoadError } from '../../core/receipt-api';
import { AiIcon, AiSuggest } from '../../core/ai-suggest';
import { ScanInput } from '../../core/scan-input';
import { CodeSchemeEditor } from './code-scheme';

/** Same rule as the server (ProductCodes.IsValid): what a barcode scanner can type. */
const CODE_PATTERN = /^[A-Za-z0-9][A-Za-z0-9-]{0,29}$/;

/** Shown on the AI buttons while the questionnaire hasn't been answered. */
const AI_OFF = 'ตอบแบบสอบถามรูปแบบรหัสก่อน (ปุ่ม “รูปแบบรหัส”) จึงจะใช้ AI แนะนำได้';

/** Same rule as the server (ProductNames.Key): names compare trimmed, spaces collapsed, ignoring case. */
function nameKey(name: string): string {
  return name.normalize('NFC').replace(/\s+/g, ' ').trim().toLowerCase();
}

/** Offered while typing the unit; units already used by the company's products come first. */
const COMMON_UNITS = ['ชิ้น', 'หลัง', 'ผืน', 'ใบ', 'ชุด', 'อัน', 'ตัว', 'คู่', 'กล่อง', 'แพ็ค', 'ม้วน', 'เมตร', 'ครั้ง', 'งาน'];

/** One warehouse row of the ยอดยกมา table. */
interface OpeningRow {
  warehouseId: string;
  name: string;
  /** On hand now (edit only): opening plus everything since. */
  onHand: number | null;
  quantity: number | null;
  unitCost: number | null;
}

@Component({
  selector: 'app-products',
  imports: [
    DecimalPipe,
    FormsModule,
    ReactiveFormsModule,
    ScanInput,
    AiIcon,
    AiSuggest,
    CodeSchemeEditor,
    CdkDrag,
    CdkDragHandle,
    CdkDropList,
    NzAutocompleteModule,
    NzButtonModule,
    NzCardModule,
    NzCheckboxModule,
    NzDatePickerModule,
    NzEmptyModule,
    NzFormModule,
    NzGridModule,
    NzIconModule,
    NzInputModule,
    NzInputNumberModule,
    NzModalModule,
    NzRadioModule,
    NzSelectModule,
    NzSwitchModule,
    NzTableModule,
    NzTagModule,
    NzTooltipModule,
  ],
  template: `
    <div class="mx-auto flex w-full max-w-310 flex-col gap-4 p-5 max-md:px-4 max-md:py-3">
      <header class="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 class="m-0 text-[20px] font-semibold">สินค้า</h1>
          <p class="mt-1 mb-0 max-w-[76ch] text-black/45">
            รหัสสินค้าตั้งเองหรือให้ AI แนะนำตามรูปแบบของร้าน ใช้อ้างอิงตอนบันทึกบัญชีและเป็นบาร์โค้ดบนป้าย — สแกนป้ายหรือพิมพ์รหัสในช่องค้นหาเพื่อหาสินค้า
          </p>
        </div>
        @if (auth.isManager()) {
          <div class="flex flex-wrap gap-2">
            <button nz-button (click)="schemeOpen.set(true)"><nz-icon nzType="setting" /> รูปแบบรหัส</button>
            <button nz-button [disabled]="!selected().size" (click)="openPrint()">
              <nz-icon nzType="printer" /> พิมพ์ป้ายบาร์โค้ด{{ selected().size ? ' (' + selected().size + ')' : '' }}
            </button>
            <span [attr.title]="activeCategories().length ? null : 'เพิ่มหมวดสินค้าก่อน'">
              <button nz-button nzType="primary" [disabled]="!activeCategories().length" (click)="openProduct(null)">
                <nz-icon nzType="plus" /> เพิ่มสินค้า
              </button>
            </span>
          </div>
        }
      </header>

      @if (auth.isManager() && schemeLoaded() && !scheme()) {
        <div class="setup">
          <app-ai-icon class="text-[20px]" />
          <div class="flex-1">
            <b>ให้ AI ช่วยตั้งรหัสหมวดและรหัสสินค้า</b>
            <div class="muted text-[13px]">ตอบแบบสอบถามสั้น ๆ ว่าร้านขายอะไร แล้ว AI จะวางรูปแบบชื่อและรหัสให้ — ยังไม่ตอบ ปุ่ม AI แนะนำจะปิดอยู่ ตั้งรหัสเองได้ตามปกติ</div>
          </div>
          <button nz-button nzType="primary" (click)="schemeOpen.set(true)">ตอบแบบสอบถาม</button>
        </div>
      }

      <div class="grid grid-cols-[260px_minmax(0,1fr)] items-start gap-4 max-[860px]:grid-cols-1">
        <nz-card class="cat-card" nzTitle="หมวดสินค้า" [nzExtra]="addCategoryBtn" [nzBodyStyle]="{ padding: '6px' }">
          <ng-template #addCategoryBtn>
            @if (auth.isManager()) {
              <button nz-button nzType="text" nzSize="small" (click)="openCategory(null)" aria-label="เพิ่มหมวดสินค้า">
                <nz-icon nzType="plus" /> เพิ่ม
              </button>
            }
          </ng-template>
          <button type="button" class="item" [class.selected]="categoryId() === null" (click)="categoryId.set(null)">
            <span class="name">ทั้งหมด</span>
            <span class="count num">{{ products().length }}</span>
          </button>
          <!-- admins drag a category by its handle to reorder; the new order is saved on drop -->
          <ul class="m-0 mt-0.5 grid list-none gap-0.5 p-0" cdkDropList [cdkDropListDisabled]="!auth.isManager()" (cdkDropListDropped)="dropCategory($event)">
            @for (c of categories(); track c.id) {
              <li class="group cat relative" cdkDrag cdkDragLockAxis="y" cdkDragBoundary=".cat-card">
                @if (auth.isManager()) {
                  <span class="drag-handle" cdkDragHandle [attr.aria-label]="'ลากเพื่อเรียงหมวด ' + c.name" title="ลากเพื่อเรียงลำดับ">
                    <nz-icon nzType="holder" />
                  </span>
                }
                <button
                  type="button"
                  class="item"
                  [class.selected]="categoryId() === c.id"
                  [class.off]="!c.active"
                  (click)="categoryId.set(c.id)"
                >
                  <span class="name">{{ c.name }}</span>
                  <span class="count num">{{ c.productCount }}</span>
                  <span class="meta">
                    <span class="num">{{ c.codePrefix }}</span>
                    @if (!c.active) {
                      · ปิดใช้งาน
                    }
                  </span>
                </button>
                @if (auth.isManager()) {
                  <button type="button" class="edit-cat" (click)="openCategory(c)" [attr.aria-label]="'แก้ไขหมวด ' + c.name">
                    <nz-icon nzType="edit" />
                  </button>
                }
              </li>
            }
          </ul>
          @if (loaded() && !categories().length) {
            <p class="muted m-0 px-3 py-2 text-[13px]">
              {{ auth.isManager() ? 'เริ่มจากเพิ่มหมวดสินค้า เช่น เตียงนอน ที่นอน' : 'ยังไม่มีหมวดสินค้า' }}
            </p>
          }
        </nz-card>

        <nz-card [nzBodyStyle]="{ padding: '0' }">
          <div class="p-3">
            <app-scan-input [(value)]="search" (scanned)="onScan($event)" />
          </div>
          <nz-table
            #pt
            nzSize="small"
            [nzData]="filtered()"
            [nzPageSize]="50"
            [nzHideOnSinglePage]="true"
            [nzLoading]="!loaded()"
            [nzScroll]="{ x: '900px' }"
            [nzNoResult]="empty"
          >
            <thead>
              <tr>
                @if (auth.isManager()) {
                  <th
                    nzWidth="40px"
                    [nzChecked]="allChecked()"
                    [nzIndeterminate]="!allChecked() && someChecked()"
                    (nzCheckedChange)="checkAll($event)"
                    aria-label="เลือกทั้งหมด"
                  ></th>
                }
                <th nzWidth="130px">รหัส</th>
                <th>ชื่อสินค้า</th>
                @if (categoryId() === null) {
                  <th nzWidth="130px">หมวด</th>
                }
                <th nzWidth="130px" nzAlign="right">คงเหลือ</th>
                <th nzWidth="110px" nzAlign="right">มูลค่าคงเหลือ</th>
                <th nzWidth="100px" nzAlign="right">ราคาขาย</th>
                @if (auth.isManager()) {
                  <th nzWidth="90px"></th>
                }
              </tr>
            </thead>
            <tbody>
              @for (p of pt.data; track p.id) {
                <tr [class.inactive]="!p.active" [class.found]="highlight() === p.id">
                  @if (auth.isManager()) {
                    <td [nzChecked]="selected().has(p.id)" (nzCheckedChange)="toggle(p.id, $event)"></td>
                  }
                  <td class="num">{{ p.code }}</td>
                  <td>
                    {{ p.name }}
                    @if (!p.trackStock) {
                      <nz-tag class="ml-1">บริการ</nz-tag>
                    }
                    @if (!p.active) {
                      <nz-tag class="ml-1">เลิกขาย</nz-tag>
                    }
                  </td>
                  @if (categoryId() === null) {
                    <td class="muted">{{ p.categoryName }}</td>
                  }
                  <td nzAlign="right">
                    @if (p.trackStock) {
                      @if (isLow(p)) {
                        <nz-tag nzColor="warning" nz-tooltip [nzTooltipTitle]="'ตั้งให้แจ้งเตือนเมื่อเหลือ ' + (p.reorderPoint | number: '1.0-4') + ' ' + p.unit">ใกล้หมด</nz-tag>
                      }
                      <span class="num" [class.negative]="p.onHand < 0">{{ p.onHand | number: '1.0-4' }}</span>
                      <span class="muted"> {{ p.unit }}</span>
                    } @else {
                      <span class="muted">—</span>
                    }
                  </td>
                  <td class="num" nzAlign="right">{{ p.trackStock ? (p.stockValue | number: '1.2-2') : '—' }}</td>
                  <td class="num" nzAlign="right">{{ p.salePrice === null ? '—' : (p.salePrice | number: '1.0-2') }}</td>
                  @if (auth.isManager()) {
                    <td nzAlign="right">
                      <button nz-button nzType="text" nzSize="small" (click)="openProduct(p)"><nz-icon nzType="edit" /> แก้ไข</button>
                    </td>
                  }
                </tr>
              }
            </tbody>
          </nz-table>
          <ng-template #empty>
            <nz-empty [nzNotFoundContent]="search().trim() ? 'ไม่พบสินค้าที่ค้นหา' : 'ยังไม่มีสินค้าในหมวดนี้'" />
          </ng-template>
        </nz-card>
      </div>
    </div>

    <!-- category -->
    <nz-modal
      [nzVisible]="categoryOpen()"
      [nzTitle]="editingCategory() ? 'แก้ไขหมวดสินค้า' : 'เพิ่มหมวดสินค้า'"
      [nzFooter]="categoryFooter"
      (nzOnCancel)="categoryOpen.set(false)"
    >
      <ng-container *nzModalContent>
        <form nz-form nzLayout="vertical" [formGroup]="categoryForm" (ngSubmit)="saveCategory()">
          <nz-form-item>
            <nz-form-label nzRequired>ชื่อหมวด</nz-form-label>
            <nz-form-control [nzExtra]="scheme()?.categoryNameHint ? 'รูปแบบ: ' + scheme()!.categoryNameHint : undefined">
              <input nz-input formControlName="name" maxlength="200" placeholder="เช่น เตียงนอน 6 ฟุต" />
            </nz-form-control>
          </nz-form-item>
          <nz-form-item>
            <nz-form-label nzRequired>รหัสหมวด</nz-form-label>
            <nz-form-control [nzExtra]="prefixHelp">
              <nz-input-group [nzSuffix]="prefixAi" [class.ai-busy]="suggesting()">
                <input nz-input class="num uppercase" formControlName="codePrefix" maxlength="6" placeholder="BED6" />
              </nz-input-group>
              <ng-template #prefixAi>
                <app-ai-suggest
                  [busy]="suggesting()"
                  [disabled]="!scheme() || !categoryName().trim()"
                  [hint]="aiTitle(!!categoryName().trim(), 'กรอกชื่อหมวดก่อน', 'ให้ AI แนะนำรหัสหมวด')"
                  (pressed)="suggestPrefix()"
                />
              </ng-template>
            </nz-form-control>
            <ng-template #prefixHelp>
              @if (prefixReason(); as r) {
                <div class="ai-note"><app-ai-icon /> {{ r }}</div>
              }
              A–Z / 0–9 ไม่เกิน 6 ตัว ห้ามซ้ำกับหมวดอื่น · เป็นตัวต้นของรหัสสินค้าที่ AI แนะนำ
              @if (editingCategory()?.productCount) {
                · สินค้าที่อยู่ในหมวดแล้วไม่เปลี่ยนรหัสตาม
              }
            </ng-template>
          </nz-form-item>
          @if (editingCategory()) {
            <nz-form-item class="mb-0">
              <div class="flex items-center gap-2">
                <nz-switch formControlName="active" id="cat-active" />
                <label for="cat-active">{{ categoryForm.controls.active.value ? 'เปิดใช้งาน' : 'ปิดใช้งาน' }}</label>
              </div>
              <div class="muted mt-1 text-[12px]">ปิดแล้วจะเลือกหมวดนี้ตอนเพิ่มสินค้าใหม่ไม่ได้ สินค้าที่อยู่ในหมวดยังอยู่ตามเดิม</div>
            </nz-form-item>
          }
          <button type="submit" hidden></button>
        </form>
      </ng-container>
      <ng-template #categoryFooter>
        <div class="flex items-center gap-2">
          @if (editingCategory(); as c) {
            @if (c.productCount === 0) {
              <button nz-button nzDanger (click)="askDeleteCategory(c)"><nz-icon nzType="delete" /> ลบ</button>
            } @else {
              <span class="muted text-left text-[12px]">มีสินค้า {{ c.productCount }} รายการ ลบไม่ได้ — ปิดใช้งานแทน</span>
            }
          }
          <span class="flex-1"></span>
          <button nz-button (click)="categoryOpen.set(false)">ยกเลิก</button>
          <button nz-button nzType="primary" [disabled]="categoryForm.invalid || suggesting()" [nzLoading]="busy()" (click)="saveCategory()">บันทึก</button>
        </div>
      </ng-template>
    </nz-modal>

    <!-- product -->
    <nz-modal
      [nzVisible]="productOpen()"
      [nzTitle]="editingProduct() ? 'แก้ไขสินค้า' : 'เพิ่มสินค้า'"
      [nzFooter]="productFooter"
      nzWidth="680px"
      (nzOnCancel)="productOpen.set(false)"
    >
      <ng-container *nzModalContent>
        <form nz-form nzLayout="vertical" [formGroup]="productForm" (ngSubmit)="saveProduct()">
          <div nz-row [nzGutter]="12">
            <nz-form-item nz-col [nzXs]="24" [nzSm]="10">
              <nz-form-label nzRequired>หมวด</nz-form-label>
              <nz-form-control>
                <nz-select formControlName="categoryId" nzShowSearch>
                  @for (c of categoryOptions(); track c.id) {
                    <nz-option [nzValue]="c.id" [nzLabel]="c.active ? c.name : c.name + ' (ปิดใช้งาน)'" [nzDisabled]="!c.active"></nz-option>
                  }
                </nz-select>
              </nz-form-control>
            </nz-form-item>
            <nz-form-item nz-col [nzXs]="24" [nzSm]="14">
              <nz-form-label nzRequired>ชื่อสินค้า</nz-form-label>
              <nz-form-control [nzErrorTip]="nameError" [nzExtra]="scheme()?.productNameHint ? 'รูปแบบ: ' + scheme()!.productNameHint : undefined">
                <input nz-input formControlName="name" maxlength="300" placeholder="เช่น Lotus Burgi ขาว" />
                <ng-template #nameError let-control>
                  @if (control.errors?.duplicate; as dup) {
                    มีสินค้าชื่อนี้แล้ว: {{ dup.code }} {{ dup.name }}{{ dup.active ? '' : ' (เลิกขาย)' }} — ตั้งชื่อให้ต่างกัน
                  } @else {
                    กรอกชื่อสินค้า
                  }
                </ng-template>
              </nz-form-control>
            </nz-form-item>
            <nz-form-item nz-col [nzSpan]="24">
              <nz-form-label nzRequired>รหัสสินค้า</nz-form-label>
              <nz-form-control [nzErrorTip]="codeError" [nzExtra]="codeHelp">
                <nz-input-group [nzSuffix]="codeAi" [class.ai-busy]="suggesting()">
                  <input nz-input class="num uppercase" formControlName="code" maxlength="30" placeholder="เช่น BED6123111" />
                </nz-input-group>
                <ng-template #codeAi>
                  <app-ai-suggest
                    [busy]="suggesting()"
                    [disabled]="!scheme() || !formName().trim() || !formCategory()"
                    [hint]="aiTitle(!!formName().trim() && !!formCategory(), 'เลือกหมวดและกรอกชื่อสินค้าก่อน', 'ให้ AI แนะนำรหัสสินค้า')"
                    (pressed)="suggestCode()"
                  />
                </ng-template>
                <ng-template #codeError let-control>
                  @if (control.errors?.duplicate; as dup) {
                    รหัสนี้ใช้กับ {{ dup }} แล้ว — ตั้งรหัสอื่น
                  } @else if (control.errors?.pattern) {
                    ใช้ได้เฉพาะ A–Z, 0–9 และ - ไม่เกิน 30 ตัว
                  } @else {
                    กรอกรหัสสินค้า หรือกด AI แนะนำ
                  }
                </ng-template>
              </nz-form-control>
              <ng-template #codeHelp>
                @if (suggestion(); as sg) {
                  <div class="parts">
                    <span class="part"><span class="muted">หมวด</span> <b class="num">{{ prefixOf(sg) }}</b></span>
                    @for (pt of sg.parts; track pt.segment) {
                      <span class="part" [class.new]="pt.term && !pt.known">
                        <span class="muted">{{ pt.label }}</span>
                        {{ pt.term ?? '—' }}
                        <b class="num">{{ pt.code }}</b>
                        @if (pt.term && !pt.known) {
                          <nz-tag nzColor="processing" class="m-0">ใหม่</nz-tag>
                        }
                      </span>
                    }
                  </div>
                  @if (sg.warning) {
                    <div class="warn">{{ sg.warning }}</div>
                  }
                  @if (sg.reason) {
                    <div class="ai-note"><app-ai-icon /> {{ sg.reason }}</div>
                  }
                  @if (suggestionUsed() && newParts()) {
                    <div>บันทึกแล้วระบบจะจำรหัส “ใหม่” ไว้ใช้กับสินค้าชิ้นต่อไป</div>
                  }
                } @else if (scheme()?.codeHint) {
                  {{ scheme()!.codeHint }}
                }
                @if (editingProduct() && codeChanged()) {
                  <div class="warn">เปลี่ยนรหัสแล้ว ป้ายบาร์โค้ดที่พิมพ์ไว้ด้วยรหัสเดิมจะสแกนไม่เจอ</div>
                }
              </ng-template>
            </nz-form-item>

            <nz-form-item nz-col [nzSpan]="24">
              <nz-form-label>ประเภท</nz-form-label>
              <nz-radio-group formControlName="trackStock" class="kind-group">
                <label nz-radio [nzValue]="true" class="kind">
                  <b>สินค้า</b>
                  <span class="muted">มีของเก็บในคลัง — ระบบนับจำนวนคงเหลือและมูลค่าให้</span>
                </label>
                <label nz-radio [nzValue]="false" class="kind">
                  <b>บริการ / ค่าใช้จ่าย</b>
                  <span class="muted">ไม่มีของในคลัง เช่น ค่าขนส่ง ค่าติดตั้ง — ไม่นับจำนวน</span>
                </label>
              </nz-radio-group>
            </nz-form-item>

            <nz-form-item nz-col [nzXs]="24" [nzSm]="8">
              <nz-form-label>หน่วยนับ</nz-form-label>
              <nz-form-control nzExtra="คำที่ต่อท้ายจำนวน เช่น 2 หลัง, 3 ผืน">
                <input nz-input formControlName="unit" maxlength="30" placeholder="ชิ้น" [nzAutocomplete]="unitList" />
                <nz-autocomplete #unitList>
                  @for (u of unitOptions(); track u) {
                    <nz-auto-option [nzValue]="u">{{ u }}</nz-auto-option>
                  }
                </nz-autocomplete>
              </nz-form-control>
            </nz-form-item>
            <nz-form-item nz-col [nzXs]="24" [nzSm]="8">
              <nz-form-label>ราคาขาย</nz-form-label>
              <nz-form-control [nzExtra]="'ต่อ 1 ' + unitLabel() + ' · พิมพ์บนป้ายได้'">
                <nz-input-number class="w-full" formControlName="salePrice" [nzMin]="0" [nzFormatter]="money" nzAddonAfter="บาท" />
              </nz-form-control>
            </nz-form-item>
            @if (productForm.controls.trackStock.value) {
              <nz-form-item nz-col [nzXs]="24" [nzSm]="8">
                <nz-form-label>แจ้งเตือนเมื่อเหลือ</nz-form-label>
                <nz-form-control nzExtra="เหลือเท่านี้หรือน้อยกว่า จะขึ้นป้าย “ใกล้หมด” · เว้นว่าง = ไม่เตือน">
                  <nz-input-number class="w-full" formControlName="reorderPoint" [nzMin]="0" [nzFormatter]="qty" [nzAddonAfter]="unitLabel()" />
                </nz-form-control>
              </nz-form-item>
            }
          </div>
          <button type="submit" hidden></button>
        </form>

        @if (productForm.controls.trackStock.value) {
          <section class="stock-box">
            <div class="flex flex-wrap items-end justify-between gap-2">
              <div>
                <div class="section-title">ยอดยกมา (จำนวนที่มีอยู่ก่อนเริ่มใช้ระบบ)</div>
                <div class="muted text-[12px]">
                  กรอกครั้งเดียวตอนเริ่มต้น หลังจากนี้จำนวนจะเพิ่ม-ลดเองจากเอกสารซื้อขายที่อนุมัติ · ไม่มีของ เว้นว่างได้
                </div>
              </div>
              <div>
                <div class="field-label">ณ วันที่</div>
                <nz-date-picker [ngModel]="openingDate()" (ngModelChange)="openingDate.set($event)" nzFormat="dd/MM/yyyy" [nzAllowClear]="false" />
              </div>
            </div>
            @if (detailLoading()) {
              <p class="muted mb-0">กำลังโหลดสต็อก…</p>
            } @else {
              <nz-table #ot class="mt-3" nzSize="small" [nzData]="openingRows()" [nzShowPagination]="false" [nzFrontPagination]="false" [nzScroll]="{ x: '560px' }">
                <thead>
                  <tr>
                    <th>คลัง</th>
                    <th nzWidth="150px">จำนวน</th>
                    <th nzWidth="170px">ต้นทุนต่อหน่วย (ก่อน VAT)</th>
                    @if (editingProduct()) {
                      <th nzWidth="110px" nzAlign="right">คงเหลือตอนนี้</th>
                    }
                  </tr>
                </thead>
                <tbody>
                  @for (r of ot.data; track r.warehouseId; let i = $index) {
                    <tr>
                      <td>{{ r.name }}</td>
                      <td>
                        <nz-input-number
                          class="w-full"
                          nzSize="small"
                          [ngModel]="r.quantity"
                          (ngModelChange)="setOpening(i, 'quantity', $event)"
                          [nzMin]="0"
                          [nzFormatter]="qty"
                          [nzAddonAfter]="unitLabel()"
                          [attr.aria-label]="'จำนวนยอดยกมา ' + r.name"
                        />
                      </td>
                      <td>
                        <nz-input-number
                          class="w-full"
                          nzSize="small"
                          [ngModel]="r.unitCost"
                          (ngModelChange)="setOpening(i, 'unitCost', $event)"
                          [nzMin]="0"
                          [nzFormatter]="money"
                          nzAddonAfter="บาท"
                          [nzStatus]="r.quantity && r.unitCost === null ? 'error' : ''"
                          [attr.aria-label]="'ต้นทุนต่อหน่วย ' + r.name"
                        />
                      </td>
                      @if (editingProduct()) {
                        <td class="num" nzAlign="right">{{ r.onHand | number: '1.0-4' }}</td>
                      }
                    </tr>
                  }
                </tbody>
              </nz-table>
              <div class="mt-2 flex flex-wrap justify-between gap-2 text-[13px]">
                <span [class.danger]="openingError()" class="muted">{{ openingError() ?? 'มูลค่ายอดยกมา ' + (openingValue() | number: '1.2-2') + ' บาท' }}</span>
                @if (editingProduct(); as p) {
                  <span class="muted">
                    คงเหลือรวม <b class="num">{{ p.onHand | number: '1.0-4' }}</b> {{ p.unit }} · ต้นทุนเฉลี่ย
                    <b class="num">{{ p.averageCost | number: '1.2-2' }}</b> · มูลค่า <b class="num">{{ p.stockValue | number: '1.2-2' }}</b> บาท
                  </span>
                }
              </div>
            }
          </section>
        }

        @if (editingProduct()) {
          <section class="mt-4 flex items-start gap-2">
            <nz-switch [formControl]="productForm.controls.active" id="product-active" />
            <div>
              <label for="product-active">{{ productForm.controls.active.value ? 'ขายอยู่' : 'เลิกขาย' }}</label>
              <div class="muted text-[12px]">ปิดเมื่อเลิกขายสินค้านี้ — จะไม่ให้เลือกในเอกสารใหม่ แต่ประวัติและยอดคงเหลือยังอยู่</div>
            </div>
          </section>
        }
      </ng-container>
      <ng-template #productFooter>
        <div class="flex gap-2">
          @if (editingProduct(); as p) {
            <button nz-button nzDanger (click)="askDeleteProduct(p)"><nz-icon nzType="delete" /> ลบ</button>
          }
          <span class="flex-1"></span>
          <button nz-button (click)="productOpen.set(false)">ยกเลิก</button>
          <button
            nz-button
            nzType="primary"
            [disabled]="productForm.invalid || suggesting() || (productForm.controls.trackStock.value && !!openingError())"
            [nzLoading]="busy()"
            (click)="saveProduct()"
          >
            บันทึก
          </button>
        </div>
      </ng-template>
    </nz-modal>

    <!-- labels -->
    <nz-modal
      [nzVisible]="printOpen()"
      nzTitle="พิมพ์ป้ายบาร์โค้ด"
      nzOkText="พิมพ์"
      [nzOkLoading]="printing()"
      nzWidth="640px"
      (nzOnOk)="print()"
      (nzOnCancel)="printOpen.set(false)"
    >
      <ng-container *nzModalContent>
        <div class="grid gap-3">
          <div nz-row [nzGutter]="12">
            <div nz-col [nzXs]="24" [nzSm]="template().sheet ? 16 : 24">
              <div class="field-label">ขนาดป้าย</div>
              <nz-select class="w-full" [ngModel]="templateId()" (ngModelChange)="templateId.set($event)">
                @for (t of templates; track t.id) {
                  <nz-option [nzValue]="t.id" [nzLabel]="t.label"></nz-option>
                }
              </nz-select>
            </div>
            @if (template().sheet) {
              <div nz-col [nzXs]="24" [nzSm]="8">
                <div class="field-label">เริ่มที่ดวงที่</div>
                <nz-input-number
                  class="w-full"
                  [ngModel]="startAt()"
                  (ngModelChange)="startAt.set($event ?? 1)"
                  [nzMin]="1"
                  [nzMax]="template().cols * template().rows"
                  [nzPrecision]="0"
                />
              </div>
            }
          </div>
          <label nz-checkbox [ngModel]="showPrice()" (ngModelChange)="showPrice.set($event)">แสดงราคาขายบนป้าย</label>
          <nz-table #lt nzSize="small" [nzData]="printItems()" [nzShowPagination]="false" [nzFrontPagination]="false">
            <thead>
              <tr>
                <th nzWidth="120px">รหัส</th>
                <th>ชื่อสินค้า</th>
                <th nzWidth="110px">จำนวนป้าย</th>
              </tr>
            </thead>
            <tbody>
              @for (p of lt.data; track p.id) {
                <tr>
                  <td class="num">{{ p.code }}</td>
                  <td>{{ p.name }}</td>
                  <td>
                    <nz-input-number
                      nzSize="small"
                      [ngModel]="copies()[p.id] ?? 1"
                      (ngModelChange)="setCopies(p.id, $event)"
                      [nzMin]="1"
                      [nzMax]="999"
                      [nzPrecision]="0"
                    />
                  </td>
                </tr>
              }
            </tbody>
          </nz-table>
          <p class="muted m-0 text-[12px]">
            รวม {{ totalLabels() }} ดวง · ในหน้าต่างพิมพ์ ให้ตั้ง <b>ขอบ: ไม่มี</b> และ <b>มาตราส่วน 100%</b> เพื่อให้ป้ายตรงกับดวงสติกเกอร์
          </p>
        </div>
      </ng-container>
    </nz-modal>

    @if (companyId; as id) {
      <app-code-scheme [(open)]="schemeOpen" [companyId]="id" [scheme]="scheme()" [categories]="categories()" (saved)="schemeSaved($event)" />
    }
  `,
  styles: `
    .item {
      display: grid;
      grid-template-columns: minmax(0, 1fr) auto;
      column-gap: 8px;
      width: 100%;
      padding: 7px 12px;
      border: 0;
      border-radius: 6px;
      background: transparent;
      text-align: left;
      line-height: 1.4;
      cursor: pointer;
      transition: background 0.15s;
    }
    .item:hover { background: var(--surface-2); }
    /* drag to reorder: the handle sits in the item's left padding */
    .cat .item { padding-left: 26px; }
    .drag-handle {
      position: absolute;
      top: 0;
      bottom: 0;
      left: 2px;
      z-index: 1;
      display: grid;
      place-items: center;
      width: 22px;
      color: var(--ink-3);
      cursor: grab;
      opacity: 0.35;
      touch-action: none;
    }
    .cat:hover .drag-handle { opacity: 1; }
    .drag-handle:active { cursor: grabbing; }
    @media (hover: none) { .drag-handle { opacity: 1; } }
    .cdk-drag-preview { border-radius: 6px; background: var(--surface); box-shadow: 0 6px 16px rgb(0 0 0 / 0.12); list-style: none; }
    .cdk-drag-placeholder { opacity: 0.3; }
    .cdk-drag-animating, .cdk-drop-list-dragging .cat:not(.cdk-drag-placeholder) { transition: transform 0.2s ease; }
    .item.selected { background: var(--accent-soft); }
    .item.selected .name { color: var(--accent); }
    .item.off .name { color: var(--ink-3); }
    .name { font-size: 14px; font-weight: 500; color: var(--ink); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .count { font-size: 12px; color: var(--ink-3); }
    .meta { grid-column: 1 / -1; font-size: 12px; color: var(--ink-3); }
    /* the edit button sits over the count, shown on hover (always on touch screens) */
    .edit-cat {
      position: absolute;
      top: 5px;
      right: 6px;
      display: grid;
      place-items: center;
      width: 26px;
      height: 26px;
      border: 0;
      border-radius: 4px;
      background: var(--surface);
      color: var(--ink-3);
      cursor: pointer;
      opacity: 0;
    }
    .group:hover .edit-cat, .edit-cat:focus-visible { opacity: 1; }
    .edit-cat:hover { color: var(--accent); }
    @media (hover: none) {
      .edit-cat { opacity: 1; }
      .item .count { visibility: hidden; }
    }
    .inactive td { color: var(--ink-3); }
    .found td { background: var(--accent-soft) !important; }
    .negative { color: var(--error); }
    .danger { color: var(--error) !important; }
    .field-label { margin-bottom: 4px; font-size: 13px; color: var(--ink-2); }
    .kind-group { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; width: 100%; }
    @media (max-width: 575px) { .kind-group { grid-template-columns: 1fr; } }
    .kind {
      display: flex;
      align-items: flex-start;
      margin: 0;
      padding: 10px 12px;
      border: 1px solid var(--line-strong);
      border-radius: 8px;
      line-height: 1.45;
    }
    .kind.ant-radio-wrapper-checked { border-color: var(--accent); background: var(--accent-soft); }
    .kind b { display: block; font-weight: 600; }
    .kind .muted { display: block; font-size: 12px; }
    .setup {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 12px;
      padding: 12px 16px;
      border: 1px solid var(--accent);
      border-radius: 8px;
      background: var(--accent-soft);
      color: var(--ink);
    }
    .setup > app-ai-icon { color: var(--accent); }
    .parts { display: flex; flex-wrap: wrap; gap: 6px; margin: 6px 0 4px; }
    .part {
      display: inline-flex;
      align-items: center;
      gap: 4px;
      padding: 2px 8px;
      border: 1px solid var(--line-strong);
      border-radius: 6px;
      color: var(--ink);
      font-size: 12px;
    }
    .part.new { border-color: var(--accent); }
    .ai-note { color: var(--ink-2); }
    .ai-note app-ai-icon { color: var(--accent); }
    .warn { color: var(--warning, #d48806); }
    .stock-box { margin-top: 4px; padding: 12px; border: 1px solid var(--line); border-radius: 8px; background: var(--surface-2); }
    .section-title { font-weight: 600; }
  `,
})
export class Products {
  private readonly api = inject(ReceiptApi);
  private readonly fb = inject(FormBuilder);
  private readonly message = inject(NzMessageService);
  private readonly confirm = injectConfirm();
  private readonly store = inject(CompanyStore);
  protected readonly auth = inject(Auth);

  protected readonly money = numberFormatter(2);
  protected readonly qty = numberFormatter(null);
  protected readonly templates = LABEL_TEMPLATES;

  protected readonly categories = signal<ProductCategory[]>([]);
  protected readonly activeCategories = computed(() => this.categories().filter((c) => c.active));
  protected readonly products = signal<Product[]>([]);
  private readonly warehouses = signal<Warehouse[]>([]);
  protected readonly loaded = signal(false);
  protected readonly busy = signal(false);

  // ---- code pattern and AI suggestions ----
  /** The pattern from the questionnaire; null = not answered, so the AI suggestions are off. */
  protected readonly scheme = signal<CodeScheme | null>(null);
  protected readonly schemeLoaded = signal(false);
  protected readonly schemeOpen = signal(false);
  protected readonly suggesting = signal(false);

  /** null = every category. */
  protected readonly categoryId = signal<string | null>(null);
  protected readonly search = signal('');
  /** The product found by the last scan. */
  protected readonly highlight = signal<string | null>(null);

  protected readonly filtered = computed(() => {
    const cat = this.categoryId();
    const q = this.search().trim().toLowerCase();
    return this.products().filter(
      (p) =>
        (cat === null || p.categoryId === cat) &&
        (!q || p.code.toLowerCase().includes(q) || p.name.toLowerCase().includes(q)),
    );
  });

  // ---- selection for printing ----
  protected readonly selected = signal<ReadonlySet<string>>(new Set());
  protected readonly allChecked = computed(
    () => this.filtered().length > 0 && this.filtered().every((p) => this.selected().has(p.id)),
  );
  protected readonly someChecked = computed(() => this.filtered().some((p) => this.selected().has(p.id)));

  // ---- category modal ----
  protected readonly categoryOpen = signal(false);
  protected readonly editingCategory = signal<ProductCategory | null>(null);
  protected readonly categoryForm = this.fb.nonNullable.group({
    name: ['', Validators.required],
    codePrefix: ['', [Validators.required, Validators.pattern(/^[A-Za-z0-9]{1,6}$/)]],
    active: true,
  });
  protected readonly categoryName = signal('');
  /** Why the AI suggested the prefix in the box. */
  protected readonly prefixReason = signal<string | null>(null);

  // ---- product modal ----
  protected readonly productOpen = signal(false);
  protected readonly editingProduct = signal<Product | null>(null);
  protected readonly productForm = this.fb.nonNullable.group({
    categoryId: ['', Validators.required],
    name: ['', [Validators.required, (c: AbstractControl) => this.duplicateName(c.value)]],
    code: ['', [Validators.required, Validators.pattern(CODE_PATTERN), (c: AbstractControl) => this.duplicateCode(c.value)]],
    unit: 'ชิ้น',
    salePrice: null as number | null,
    reorderPoint: null as number | null,
    trackStock: true,
    active: true,
  });
  protected readonly formCategory = signal('');
  protected readonly formName = signal('');
  private readonly formCode = signal('');
  private readonly formUnit = signal('ชิ้น');
  /** The last AI suggestion for the product in the modal. */
  protected readonly suggestion = signal<ProductCodeSuggestion | null>(null);
  /** The code in the box is still the suggested one, so its parts are sent along to be learned. */
  protected readonly suggestionUsed = computed(() => this.suggestion()?.code === this.formCode().trim().toUpperCase());
  protected readonly newParts = computed(() => this.suggestion()?.parts.some((p) => p.term && !p.known) ?? false);
  protected readonly codeChanged = computed(() => {
    const p = this.editingProduct();
    return !!p && this.formCode().trim().toUpperCase() !== p.code;
  });
  protected readonly unitLabel = computed(() => this.formUnit().trim() || 'ชิ้น');
  /** Active categories, plus the product's own one if it was switched off since. */
  protected readonly categoryOptions = computed(() => {
    const own = this.editingProduct()?.categoryId;
    return this.categories().filter((c) => c.active || c.id === own);
  });
  protected readonly unitOptions = computed(() => {
    const typed = this.formUnit().trim();
    const all = [...new Set([...this.products().map((p) => p.unit), ...COMMON_UNITS])];
    // A whole unit in the box (the default "ชิ้น") still offers every other one.
    return !typed || all.includes(typed) ? all : all.filter((u) => u.includes(typed));
  });

  // ---- ยอดยกมา ----
  protected readonly openingRows = signal<OpeningRow[]>([]);
  protected readonly openingDate = signal<Date | null>(new Date());
  /** Editing: the product's stock is still loading; nothing is sent for it until it has. */
  protected readonly detailLoading = signal(false);
  protected readonly openingValue = computed(() =>
    this.openingRows().reduce((sum, r) => sum + (r.quantity ?? 0) * (r.unitCost ?? 0), 0),
  );
  protected readonly openingError = computed<string | null>(() => {
    const rows = this.openingRows().filter((r) => r.quantity);
    if (rows.some((r) => r.unitCost === null)) return 'กรอกต้นทุนต่อหน่วยของคลังที่มีจำนวน (ใช้คิดมูลค่าสินค้าคงเหลือ)';
    if (rows.length && !this.openingDate()) return 'ระบุวันที่ของยอดยกมา';
    return null;
  });

  // ---- label printing ----
  protected readonly printOpen = signal(false);
  protected readonly printing = signal(false);
  protected readonly templateId = signal(LABEL_TEMPLATES[0].id);
  protected readonly template = computed(() => LABEL_TEMPLATES.find((t) => t.id === this.templateId()) ?? LABEL_TEMPLATES[0]);
  protected readonly showPrice = signal(false);
  protected readonly startAt = signal(1);
  protected readonly copies = signal<Partial<Record<string, number>>>({});
  protected readonly printItems = computed(() => this.products().filter((p) => this.selected().has(p.id)));
  protected readonly totalLabels = computed(() =>
    this.printItems().reduce((sum, p) => sum + (this.copies()[p.id] ?? 1), 0),
  );

  constructor() {
    this.productForm.controls.categoryId.valueChanges.pipe(takeUntilDestroyed()).subscribe((id) => this.formCategory.set(id));
    this.productForm.controls.unit.valueChanges.pipe(takeUntilDestroyed()).subscribe((u) => this.formUnit.set(u));
    this.productForm.controls.name.valueChanges.pipe(takeUntilDestroyed()).subscribe((n) => this.formName.set(n));
    this.productForm.controls.code.valueChanges.pipe(takeUntilDestroyed()).subscribe((c) => this.formCode.set(c));
    this.categoryForm.controls.name.valueChanges.pipe(takeUntilDestroyed()).subscribe((n) => this.categoryName.set(n));

    effect(() => {
      const companyId = this.store.current()?.id;
      untracked(() => {
        this.categoryId.set(null);
        this.selected.set(new Set());
        this.highlight.set(null);
        if (companyId) this.load(companyId);
      });
    });
  }

  protected get companyId(): string | null {
    return this.store.current()?.id ?? null;
  }

  private load(companyId = this.companyId) {
    if (!companyId) return;
    this.schemeLoaded.set(false);
    this.api.codeScheme(companyId).subscribe({
      next: (s) => {
        this.scheme.set(s);
        this.schemeLoaded.set(true);
      },
      // The AI suggestions just stay off; the rest of the page works without them.
      error: () => this.scheme.set(null),
    });
    forkJoin([
      this.api.productCategories(companyId),
      this.api.products(companyId),
      this.api.warehouses(companyId),
    ]).subscribe({
      next: ([c, p, w]) => {
        this.categories.set(c);
        this.products.set(p);
        this.warehouses.set(w);
        this.loaded.set(true);
        // Forget selections of products that are gone.
        const ids = new Set(p.map((x) => x.id));
        this.selected.update((s) => new Set([...s].filter((id) => ids.has(id))));
      },
      error: (e) => {
        this.loaded.set(true);
        this.message.error(stockLoadError(e));
      },
    });
  }

  /** Another product of the company already has this name (the server refuses it too). */
  private duplicateName(value: string): ValidationErrors | null {
    const key = nameKey(value ?? '');
    const own = this.editingProduct()?.id;
    const same = key ? this.products().find((p) => p.id !== own && nameKey(p.name) === key) : undefined;
    return same ? { duplicate: { code: same.code, name: same.name, active: same.active } } : null;
  }

  /** Another product of the company already has this code (the server refuses it too). */
  private duplicateCode(value: string): ValidationErrors | null {
    const code = (value ?? '').trim().toUpperCase();
    const own = this.editingProduct()?.id;
    const same = code ? this.products().find((p) => p.id !== own && p.code === code) : undefined;
    return same ? { duplicate: `${same.code} ${same.name}` } : null;
  }

  protected isLow(p: Product): boolean {
    return p.trackStock && p.active && p.reorderPoint !== null && p.onHand <= p.reorderPoint;
  }

  // ---------------- scanning ----------------

  protected onScan(text: string) {
    const companyId = this.companyId;
    if (!companyId) return;
    this.api.lookupProduct(companyId, text).subscribe({
      next: (p) => {
        this.categoryId.set(null);
        this.search.set(p.code);
        this.highlight.set(p.id);
      },
      error: (e) => {
        this.highlight.set(null);
        // Enter after typing part of a name is a search, not a failed scan.
        if (!this.filtered().length) this.message.warning(errorMessage(e));
      },
    });
  }

  // ---------------- selection ----------------

  protected toggle(id: string, on: boolean) {
    this.selected.update((s) => {
      const next = new Set(s);
      if (on) next.add(id);
      else next.delete(id);
      return next;
    });
  }

  protected checkAll(on: boolean) {
    const ids = this.filtered().map((p) => p.id);
    this.selected.update((s) => {
      const next = new Set(s);
      for (const id of ids) {
        if (on) next.add(id);
        else next.delete(id);
      }
      return next;
    });
  }

  // ---------------- categories ----------------

  protected openCategory(c: ProductCategory | null) {
    this.editingCategory.set(c);
    this.categoryForm.reset({ name: c?.name ?? '', codePrefix: c?.codePrefix ?? '', active: c?.active ?? true });
    this.categoryName.set(this.categoryForm.controls.name.value);
    this.prefixReason.set(null);
    this.categoryOpen.set(true);
  }

  // ---------------- AI suggestions ----------------

  /** Tooltip of an AI button: what it does, or why it is off. */
  protected aiTitle(ready: boolean, missing: string, action: string): string {
    if (!this.scheme()) return AI_OFF;
    return ready ? action : missing;
  }

  protected suggestPrefix() {
    const companyId = this.companyId;
    const name = this.categoryName().trim();
    if (!companyId || !name || !this.scheme() || this.suggesting()) return;
    this.lockWhileSuggesting(this.categoryForm);
    this.api.suggestCategoryCode(companyId, name, this.editingCategory()?.id ?? null).subscribe({
      next: (s) => {
        this.unlock(this.categoryForm);
        this.categoryForm.controls.codePrefix.setValue(s.codePrefix);
        this.categoryForm.controls.codePrefix.markAsDirty();
        this.prefixReason.set(s.reason);
      },
      error: (e) => {
        this.unlock(this.categoryForm);
        this.message.error(errorMessage(e));
      },
    });
  }

  protected suggestCode() {
    const companyId = this.companyId;
    const v = this.productForm.getRawValue();
    if (!companyId || !v.name.trim() || !v.categoryId || !this.scheme() || this.suggesting()) return;
    this.lockWhileSuggesting(this.productForm);
    this.api.suggestProductCode(companyId, v.categoryId, v.name.trim(), this.editingProduct()?.id ?? null).subscribe({
      next: (s) => {
        this.unlock(this.productForm);
        this.suggestion.set(s);
        this.productForm.controls.code.setValue(s.code);
        this.productForm.controls.code.markAsDirty();
      },
      error: (e) => {
        this.unlock(this.productForm);
        this.message.error(errorMessage(e));
      },
    });
  }

  /** Nothing in the form can change while the AI works on it: its answer is built from what was there. */
  private lockWhileSuggesting(form: FormGroup) {
    this.suggesting.set(true);
    form.disable({ emitEvent: false });
  }

  private unlock(form: FormGroup) {
    form.enable({ emitEvent: false });
    this.suggesting.set(false);
  }

  /** The category part of a suggested code: what comes before its parts. */
  protected prefixOf(s: ProductCodeSuggestion): string {
    const tail = s.parts.map((p) => p.code).join('');
    const at = s.code.lastIndexOf(tail);
    return at >= 0 && tail ? s.code.slice(0, at) : s.code;
  }

  protected schemeSaved(s: CodeScheme | null) {
    this.scheme.set(s);
    // Saving may have created categories.
    this.load();
  }

  protected saveCategory() {
    const companyId = this.companyId;
    if (!companyId || this.categoryForm.invalid || this.busy()) return;
    const v = this.categoryForm.getRawValue();
    const c = this.editingCategory();
    const body = { name: v.name.trim(), codePrefix: v.codePrefix.trim().toUpperCase(), ...(c ? { active: v.active } : {}) };
    const req = c
      ? this.api.updateProductCategory(companyId, c.id, body)
      : this.api.createProductCategory(companyId, body);
    this.busy.set(true);
    req.subscribe({
      next: (saved) => {
        this.busy.set(false);
        this.categoryOpen.set(false);
        if (!c) this.categoryId.set(saved.id);
        this.message.success('บันทึกแล้ว');
        this.load();
      },
      error: (e) => {
        this.busy.set(false);
        this.message.error(errorMessage(e));
      },
    });
  }

  /** Dropped after a drag: show the new order at once and save it; on failure, reload what the server has. */
  protected dropCategory(e: CdkDragDrop<unknown>) {
    const companyId = this.companyId;
    if (!companyId || e.previousIndex === e.currentIndex) return;
    const list = [...this.categories()];
    moveItemInArray(list, e.previousIndex, e.currentIndex);
    this.categories.set(list);
    this.api.reorderProductCategories(companyId, list.map((c) => c.id)).subscribe({
      next: () => this.message.success('บันทึกลำดับหมวดแล้ว'),
      error: (err) => {
        this.message.error(errorMessage(err));
        this.load();
      },
    });
  }

  protected askDeleteCategory(c: ProductCategory) {
    this.confirm({
      title: `ลบหมวด ${c.name} ?`,
      okText: 'ลบ',
      danger: true,
      onOk: () => {
        const companyId = this.companyId;
        if (!companyId) return;
        this.api.deleteProductCategory(companyId, c.id).subscribe({
          next: () => {
            this.categoryOpen.set(false);
            if (this.categoryId() === c.id) this.categoryId.set(null);
            this.message.success(`ลบหมวด ${c.name} แล้ว`);
            this.load();
          },
          error: (e) => this.message.error(errorMessage(e)),
        });
      },
    });
  }

  // ---------------- products ----------------

  protected openProduct(p: Product | null) {
    const current = this.categoryId();
    const defaultCategory = this.activeCategories().some((c) => c.id === current)
      ? current!
      : (this.activeCategories()[0]?.id ?? '');
    this.editingProduct.set(p);
    this.productForm.reset({
      categoryId: p?.categoryId ?? defaultCategory,
      name: p?.name ?? '',
      code: p?.code ?? '',
      unit: p?.unit ?? 'ชิ้น',
      salePrice: p?.salePrice ?? null,
      reorderPoint: p?.reorderPoint ?? null,
      trackStock: p?.trackStock ?? true,
      active: p?.active ?? true,
    });
    this.formCategory.set(this.productForm.controls.categoryId.value);
    this.formName.set(this.productForm.controls.name.value);
    this.formCode.set(this.productForm.controls.code.value);
    this.formUnit.set(this.productForm.controls.unit.value);
    this.suggestion.set(null);

    // ยอดยกมา: a blank row per active warehouse for a new product; the stored one for an existing product.
    this.openingDate.set(new Date());
    this.openingRows.set(
      this.warehouses()
        .filter((w) => w.active)
        .map((w) => ({ warehouseId: w.id, name: w.name, onHand: null, quantity: null, unitCost: null })),
    );
    this.detailLoading.set(!!p);
    if (p && this.companyId) {
      this.api.product(this.companyId, p.id).subscribe({
        next: (d) => this.fillStock(d),
        error: (e) => {
          this.productOpen.set(false);
          this.message.error(errorMessage(e));
        },
      });
    }
    this.productOpen.set(true);
  }

  private fillStock(d: ProductDetail) {
    if (this.editingProduct()?.id !== d.product.id) return;
    this.editingProduct.set(d.product);
    this.openingDate.set(toDate(d.openingDate) ?? new Date());
    this.openingRows.set(
      d.warehouses.map((w) => ({
        warehouseId: w.warehouseId,
        name: w.active ? w.warehouseName : `${w.warehouseName} (ปิดใช้งาน)`,
        onHand: w.onHand,
        quantity: w.openingQuantity,
        unitCost: w.openingUnitCost,
      })),
    );
    this.detailLoading.set(false);
  }

  protected setOpening(index: number, field: 'quantity' | 'unitCost', value: number | null) {
    this.openingRows.update((rows) => rows.map((r, i) => (i === index ? { ...r, [field]: value } : r)));
  }

  protected saveProduct() {
    const companyId = this.companyId;
    if (!companyId || this.productForm.invalid || this.busy()) return;
    const v = this.productForm.getRawValue();
    if (v.trackStock && this.openingError()) return;
    const openings = this.openingRows()
      .filter((r) => r.quantity)
      .map((r) => ({ warehouseId: r.warehouseId, quantity: r.quantity!, unitCost: r.unitCost }));
    const body = {
      ...v,
      name: v.name.trim(),
      code: v.code.trim().toUpperCase(),
      // Typed over the suggestion: nothing to learn from it.
      codeParts: this.suggestionUsed() ? this.suggestion()!.parts.map(({ segment, term, code }) => ({ segment, term, code })) : null,
      unit: v.unit.trim() || null,
      reorderPoint: v.trackStock ? v.reorderPoint : null,
      openingDate: openings.length ? fromDate(this.openingDate()) : null,
      // Still loading the stored ยอดยกมา: leave it as it is rather than wipe it.
      openings: this.detailLoading() ? null : openings,
    };
    const p = this.editingProduct();
    const req = p ? this.api.updateProduct(companyId, p.id, body) : this.api.createProduct(companyId, body);
    this.busy.set(true);
    req.subscribe({
      next: (saved) => {
        this.busy.set(false);
        this.productOpen.set(false);
        this.message.success(p ? 'บันทึกแล้ว' : `เพิ่ม ${saved.product.code} ${saved.product.name} แล้ว`);
        this.load();
      },
      error: (e) => {
        this.busy.set(false);
        this.message.error(errorMessage(e));
      },
    });
  }

  protected askDeleteProduct(p: Product) {
    this.confirm({
      title: `ลบสินค้า ${p.code} ${p.name} ?`,
      content: 'ยอดยกมาของสินค้านี้จะถูกลบด้วย · ถ้าเลิกขายแต่อยากเก็บประวัติไว้ ให้ตั้งเป็นเลิกขายแทน',
      okText: 'ลบ',
      danger: true,
      onOk: () => {
        const companyId = this.companyId;
        if (!companyId) return;
        this.api.deleteProduct(companyId, p.id).subscribe({
          next: () => {
            this.productOpen.set(false);
            this.message.success(`ลบ ${p.code} แล้ว`);
            this.load();
          },
          error: (e) => this.message.error(errorMessage(e)),
        });
      },
    });
  }

  // ---------------- labels ----------------

  protected openPrint() {
    this.startAt.set(1);
    this.printOpen.set(true);
  }

  protected setCopies(id: string, n: number | null) {
    this.copies.update((c) => ({ ...c, [id]: Math.max(1, Math.floor(n ?? 1)) }));
  }

  protected async print() {
    const items = this.printItems().map((p) => ({
      code: p.code,
      name: p.name,
      salePrice: p.salePrice,
      copies: this.copies()[p.id] ?? 1,
    }));
    this.printing.set(true);
    try {
      await printLabels(items, { template: this.template(), showPrice: this.showPrice(), startAt: this.startAt() });
      this.printOpen.set(false);
    } catch {
      this.message.error('สร้างป้ายไม่สำเร็จ');
    } finally {
      this.printing.set(false);
    }
  }
}
