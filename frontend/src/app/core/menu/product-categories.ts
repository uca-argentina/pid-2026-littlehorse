/**
 * The three tabs a customer's menu is split into (US-14), and what each one is
 * called on screen. The API speaks English and the venue does not.
 *
 * Written by hand, like staff-roles.ts: the contract types `category` as a
 * plain string, so there is nothing to generate from. A fixed list in code,
 * not a table — the venue does not invent its own categories.
 */
export const PRODUCT_CATEGORIES = ['Drink', 'Beer', 'NonAlcoholic'] as const;

export type ProductCategory = (typeof PRODUCT_CATEGORIES)[number];

export interface ProductCategoryDescription {
  readonly category: ProductCategory;
  readonly name: string;
}

export const PRODUCT_CATEGORY_DESCRIPTIONS: readonly ProductCategoryDescription[] = [
  { category: 'Drink', name: 'Tragos' },
  { category: 'Beer', name: 'Cervezas' },
  { category: 'NonAlcoholic', name: 'Sin alcohol' },
];

const NAMES = new Map<string, string>(
  PRODUCT_CATEGORY_DESCRIPTIONS.map((description) => [description.category, description.name]),
);

/**
 * Falls back to whatever the API sent rather than to "desconocida": a category
 * added on the server and not yet here should read as itself, not as a bug.
 */
export function productCategoryName(category: string): string {
  return NAMES.get(category) ?? category;
}
