/**
 * Thousands separators for nz-input-number: `[nzFormatter]="money"`. The default parser already drops commas.
 *
 * nz-input-number also runs the formatter on every keystroke, with the parsed number. Formatting that number
 * would eat what is still being typed ("1.0" on the way to "1.05" would become "1"), so while the field has focus
 * the typed text is kept and only its integer part is regrouped. Otherwise (loaded values, after blur) the number
 * gets the full format with `decimals` places, or up to 4 when `decimals` is null.
 */
export function numberFormatter(decimals: number | null): (value: number) => string {
  const fmt = new Intl.NumberFormat('en-US', {
    minimumFractionDigits: decimals ?? 0,
    maximumFractionDigits: decimals ?? 4,
  });
  return (value: number) => {
    const el = document.activeElement;
    // Only the field being typed in: another input may be focused while code sets this one (e.g. a computed total).
    if (
      el instanceof HTMLInputElement &&
      el.classList.contains('ant-input-number-input') &&
      Number(el.value.replace(/,/g, '')) === value
    ) {
      return groupTyped(el.value);
    }
    return fmt.format(value);
  };
}

/** "1234567.5" or "1,23,4567.5" → "1,234,567.5", leaving the sign and decimal part as typed. */
export function groupTyped(text: string): string {
  const raw = text.replace(/,/g, '').trim();
  const match = /^(-?)(\d*)(\.\d*)?$/.exec(raw);
  if (!match) return text;
  const [, sign, int, frac = ''] = match;
  return sign + int.replace(/\B(?=(\d{3})+(?!\d))/g, ',') + frac;
}
