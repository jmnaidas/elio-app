// Decimal strings -> scaled integers. No floating-point arithmetic in live totals.
export const quantityPattern = /^\d{1,6}(\.\d{1,4})?$/;
export const pricePattern = /^\d{1,12}(\.\d{1,2})?$/;
export const maxCents = 99999999999999n;
function scaled(value: string, scale: number): bigint {
  const [whole, fraction = ''] = value.split('.');
  return BigInt(whole) * 10n ** BigInt(scale) + BigInt(fraction.padEnd(scale, '0'));
}
export function lineCents(quantity: string, price: string): bigint | null {
  if (!quantityPattern.test(quantity) || !pricePattern.test(price)) return null;
  const units = scaled(quantity, 4);
  if (units <= 0n) return null;
  // Nonnegative values: adding half before integer division rounds ties away from zero.
  const cents = (units * scaled(price, 2) + 5000n) / 10000n;
  return cents <= maxCents ? cents : null;
}
export function money(cents: bigint): string {
  return `${cents / 100n}.${(cents % 100n).toString().padStart(2, '0')}`;
}
