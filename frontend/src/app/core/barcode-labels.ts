/**
 * Product labels with a Code 128 barcode of the product code, printed through a hidden iframe so the app's own
 * styles and layout stay out of the printout. Sizes are in millimetres; the browser must print at 100% scale with
 * no margins (Chrome uses the @page rule below unless the print dialog overrides it).
 */

export interface LabelTemplate {
  id: string;
  label: string;
  pageWidth: number;
  pageHeight: number;
  cols: number;
  rows: number;
  width: number;
  height: number;
  marginTop: number;
  marginLeft: number;
  /** A sheet of stickers: a partly used one can be fed again, starting further down. */
  sheet: boolean;
}

export const LABEL_TEMPLATES: LabelTemplate[] = [
  {
    id: 'a4-3x8',
    label: 'A4 สติกเกอร์ 24 ดวง (3×8, 70×37 มม.)',
    pageWidth: 210,
    pageHeight: 297,
    cols: 3,
    rows: 8,
    width: 70,
    height: 37,
    marginTop: 0.5,
    marginLeft: 0,
    sheet: true,
  },
  {
    id: 'a4-4x10',
    label: 'A4 สติกเกอร์ 40 ดวง (4×10, 52.5×29.7 มม.)',
    pageWidth: 210,
    pageHeight: 297,
    cols: 4,
    rows: 10,
    width: 52.5,
    height: 29.7,
    marginTop: 0,
    marginLeft: 0,
    sheet: true,
  },
  {
    id: 'roll-50x30',
    label: 'เครื่องพิมพ์ฉลาก ม้วน 50×30 มม.',
    pageWidth: 50,
    pageHeight: 30,
    cols: 1,
    rows: 1,
    width: 50,
    height: 30,
    marginTop: 0,
    marginLeft: 0,
    sheet: false,
  },
];

export interface LabelItem {
  code: string;
  name: string;
  salePrice: number | null;
  copies: number;
}

export interface LabelOptions {
  template: LabelTemplate;
  showPrice: boolean;
  /** 1-based position of the first label on the first sheet (skips stickers already used). */
  startAt: number;
}

const price = new Intl.NumberFormat('th-TH', { maximumFractionDigits: 2 });

export async function printLabels(items: LabelItem[], o: LabelOptions): Promise<void> {
  const { default: JsBarcode } = await import('jsbarcode');
  const t = o.template;
  const perPage = t.cols * t.rows;
  const skip = t.sheet ? Math.min(Math.max(o.startAt, 1), perPage) - 1 : 0;

  // One barcode per product, reused for its copies.
  const labels: string[] = [];
  for (const item of items) {
    const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    JsBarcode(svg, item.code, { format: 'CODE128', displayValue: false, margin: 10, width: 1, height: 40 });
    // Stretch to the box: Code 128 reads by the ratio between bars, which scaling keeps.
    svg.removeAttribute('width');
    svg.removeAttribute('height');
    svg.setAttribute('preserveAspectRatio', 'none');
    const html = `
      <div class="name">${escapeHtml(item.name)}</div>
      <div class="bars">${svg.outerHTML}</div>
      <div class="foot"><span class="code">${escapeHtml(item.code)}</span>${
        o.showPrice && item.salePrice != null ? `<span class="price">฿${price.format(item.salePrice)}</span>` : ''
      }</div>`;
    for (let i = 0; i < Math.max(1, Math.floor(item.copies)); i++) labels.push(html);
  }

  const pages: string[] = [];
  const slots = [...Array<string | null>(skip).fill(null), ...labels];
  for (let p = 0; p * perPage < slots.length; p++) {
    const cells = slots.slice(p * perPage, (p + 1) * perPage).map((label, i) => {
      if (label === null) return '';
      const col = i % t.cols;
      const row = Math.floor(i / t.cols);
      const left = t.marginLeft + col * t.width;
      const top = t.marginTop + row * t.height;
      return `<div class="label" style="left:${left}mm;top:${top}mm">${label}</div>`;
    });
    pages.push(`<div class="page">${cells.join('')}</div>`);
  }

  const small = t.height < 32;
  const doc = `<!doctype html>
<html lang="th"><head><meta charset="utf-8"><title>ป้ายสินค้า</title>
<link href="https://fonts.googleapis.com/css2?family=IBM+Plex+Mono:wght@500&family=IBM+Plex+Sans+Thai:wght@500;600&display=swap" rel="stylesheet">
<style>
  @page { size: ${t.pageWidth}mm ${t.pageHeight}mm; margin: 0; }
  * { box-sizing: border-box; }
  html, body { margin: 0; padding: 0; background: #fff; color: #000; }
  body { font-family: 'IBM Plex Sans Thai', sans-serif; -webkit-print-color-adjust: exact; print-color-adjust: exact; }
  .page { position: relative; width: ${t.pageWidth}mm; height: ${t.pageHeight}mm; overflow: hidden; break-after: page; }
  .page:last-child { break-after: auto; }
  .label { position: absolute; width: ${t.width}mm; height: ${t.height}mm; padding: ${small ? '1.5mm 2mm' : '2.5mm 3mm'};
           display: flex; flex-direction: column; gap: 0.8mm; overflow: hidden; }
  .name { font-size: ${small ? '7pt' : '8.5pt'}; font-weight: 600; line-height: 1.25; max-height: 2.5em; overflow: hidden;
          display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; }
  .bars { flex: 1; min-height: 0; }
  .bars svg { display: block; width: 100%; height: 100%; }
  .foot { display: flex; justify-content: space-between; align-items: baseline; gap: 2mm; }
  .code { font-family: 'IBM Plex Mono', monospace; font-size: ${small ? '7pt' : '8pt'}; font-weight: 500; letter-spacing: 0.04em; }
  .price { font-size: ${small ? '8pt' : '10pt'}; font-weight: 600; }
</style></head>
<body>${pages.join('')}</body></html>`;

  // A frame left over from a print whose afterprint never fired.
  document.querySelectorAll('iframe[data-label-print]').forEach((f) => f.remove());
  const frame = document.createElement('iframe');
  frame.setAttribute('data-label-print', '');
  frame.setAttribute('aria-hidden', 'true');
  frame.style.cssText = 'position:fixed;right:0;bottom:0;width:0;height:0;border:0;visibility:hidden';
  document.body.appendChild(frame);
  const win = frame.contentWindow!;
  win.document.open();
  win.document.write(doc);
  win.document.close();

  // Thai text needs the web font before printing, or it prints in a fallback font with different widths.
  await new Promise<void>((resolve) => {
    if (win.document.readyState === 'complete') resolve();
    else win.addEventListener('load', () => resolve(), { once: true });
  });
  await Promise.race([win.document.fonts.ready, new Promise((r) => setTimeout(r, 3000))]);

  win.addEventListener('afterprint', () => frame.remove(), { once: true });
  win.focus();
  win.print();
}

function escapeHtml(s: string): string {
  return s.replace(/[&<>"']/g, (c) => `&#${c.charCodeAt(0)};`);
}
