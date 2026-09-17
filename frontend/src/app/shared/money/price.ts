/**
 * Argentine format, with both decimals always. Built once rather than per call:
 * a menu redraws on every keystroke of the search, and a formatter is not free.
 */
const PRICE = new Intl.NumberFormat('es-AR', {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});

/** What a number looks like on screen. The "$" belongs to the template. */
export function formatPrice(amount: number): string {
  return PRICE.format(amount);
}
