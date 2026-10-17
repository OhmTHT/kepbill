import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { registerLocaleData } from '@angular/common';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import th from '@angular/common/locales/th';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import {
  AppstoreOutline,
  BankOutline,
  BarcodeOutline,
  BookOutline,
  CalendarOutline,
  CameraOutline,
  CheckCircleFill,
  CheckOutline,
  ClockCircleOutline,
  CloseOutline,
  CodeOutline,
  DatabaseOutline,
  DeleteOutline,
  DownloadOutline,
  DownOutline,
  EditOutline,
  ExpandOutline,
  FileTextOutline,
  HolderOutline,
  InboxOutline,
  KeyOutline,
  LeftOutline,
  LockOutline,
  LogoutOutline,
  MailOutline,
  MenuOutline,
  MinusCircleOutline,
  PlusOutline,
  PrinterOutline,
  ReloadOutline,
  RightOutline,
  RobotFill,
  RobotOutline,
  RotateRightOutline,
  SaveOutline,
  SearchOutline,
  SettingOutline,
  ShopOutline,
  ShoppingCartOutline,
  StarFill,
  StarOutline,
  TagsOutline,
  TeamOutline,
  UnlockOutline,
  UserOutline,
  ZoomInOutline,
  ZoomOutOutline,
} from '@ant-design/icons-angular/icons';
import { provideNzConfig } from 'ng-zorro-antd/core/config';
import { NzI18nInterface, provideNzI18n, th_TH } from 'ng-zorro-antd/i18n';
import { provideNzIcons } from 'ng-zorro-antd/icon';

import { routes } from './app.routes';
import { authInterceptor } from './core/auth';

registerLocaleData(th);

/** ng-zorro's th_TH lacks the quarter-range placeholder and logs a warning whenever a date picker renders. */
const thLocale: NzI18nInterface = {
  ...th_TH,
  DatePicker: {
    ...th_TH.DatePicker,
    lang: {
      ...th_TH.DatePicker.lang,
      rangeQuarterPlaceholder: ['ไตรมาสเริ่มต้น', 'ไตรมาสสิ้นสุด'],
    },
  },
};

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideHttpClient(withFetch(), withInterceptors([authInterceptor])),
    provideRouter(routes, withComponentInputBinding()),
    provideNzI18n(thLocale),
    // Icons are bundled statically (no runtime fetching from /assets).
    provideNzIcons([
      AppstoreOutline,
      BankOutline,
      BarcodeOutline,
      BookOutline,
      CalendarOutline,
      CameraOutline,
      CheckCircleFill,
      CheckOutline,
      ClockCircleOutline,
      CloseOutline,
      CodeOutline,
      DatabaseOutline,
      DeleteOutline,
      DownloadOutline,
      DownOutline,
      EditOutline,
      ExpandOutline,
      FileTextOutline,
      HolderOutline,
      InboxOutline,
      KeyOutline,
      LeftOutline,
      LockOutline,
      LogoutOutline,
      MailOutline,
      MenuOutline,
      MinusCircleOutline,
      PlusOutline,
      PrinterOutline,
      ReloadOutline,
      RightOutline,
      RobotFill,
      RobotOutline,
      RotateRightOutline,
      SaveOutline,
      SearchOutline,
      SettingOutline,
      ShopOutline,
      ShoppingCartOutline,
      StarFill,
      StarOutline,
      TagsOutline,
      TeamOutline,
      UnlockOutline,
      UserOutline,
      ZoomInOutline,
      ZoomOutOutline,
    ]),
    provideNzConfig({ message: { nzDuration: 3000, nzMaxStack: 3 } }),
  ],
};
