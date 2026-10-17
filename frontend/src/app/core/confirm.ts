import { inject } from '@angular/core';
import { NzModalService } from 'ng-zorro-antd/modal';

export interface ConfirmOptions {
  title: string;
  content?: string;
  okText: string;
  /** Red OK button, for deletes and other changes that can't be undone. */
  danger?: boolean;
  onOk: () => void;
}

/**
 * A confirm dialog. Used instead of nz-popconfirm everywhere: a popover anchored to its button gets clipped or
 * pushed off-screen near an edge (sticky footers, table rows, phones).
 *
 * Call it in the injection context of a component that imports NzModalModule, which is where NzModalService is
 * provided.
 */
export function injectConfirm() {
  const modal = inject(NzModalService);
  return (o: ConfirmOptions) =>
    modal.confirm({
      nzTitle: o.title,
      nzContent: o.content,
      nzOkText: o.okText,
      nzOkDanger: o.danger ?? false,
      nzCancelText: 'ยกเลิก',
      nzOnOk: () => o.onOk(),
    });
}
