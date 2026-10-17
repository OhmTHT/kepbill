import { Component, computed, input } from '@angular/core';
import { NzIconModule } from 'ng-zorro-antd/icon';
import { NzTooltipModule } from 'ng-zorro-antd/tooltip';

/**
 * Active / switched-off mark for table rows. The two states differ in shape as well as colour (filled check vs.
 * outlined minus), so they read without colour; the words are in the tooltip and for screen readers.
 */
@Component({
  selector: 'app-active-mark',
  imports: [NzIconModule, NzTooltipModule],
  template: `
    <span class="mark" [class.on]="active()" role="img" [attr.aria-label]="label()" nz-tooltip [nzTooltipTitle]="label()">
      <nz-icon [nzType]="active() ? 'check-circle' : 'minus-circle'" [nzTheme]="active() ? 'fill' : 'outline'" />
    </span>
  `,
  styles: `
    .mark {
      display: inline-flex;
      font-size: 17px;
      color: var(--ink-3);
      cursor: default;
    }
    .mark.on {
      color: var(--accent);
    }
  `,
})
export class ActiveMark {
  readonly active = input.required<boolean>();
  protected readonly label = computed(() => (this.active() ? 'ใช้งาน' : 'ปิดใช้'));
}
