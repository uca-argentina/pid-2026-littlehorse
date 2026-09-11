// @ts-check
const eslint = require('@eslint/js');
const { defineConfig } = require('eslint/config');
const tseslint = require('typescript-eslint');
const angular = require('angular-eslint');

module.exports = defineConfig([
  {
    // Generated from the backend's OpenAPI document by "pnpm run generate:api".
    // Linting it is pointless: every finding comes back on the next generation,
    // and the file is not ours to style.
    ignores: ['src/app/core/api/schema.d.ts'],
  },
  {
    files: ['**/*.ts'],
    extends: [
      eslint.configs.recommended,
      tseslint.configs.recommended,
      tseslint.configs.stylistic,
      angular.configs.tsRecommended,
    ],
    processor: angular.processInlineTemplates,
    rules: {
      // Every element and attribute we own is prefixed, so a drinkit-* tag is
      // never confused with one from a component library.
      '@angular-eslint/directive-selector': [
        'error',
        { type: 'attribute', prefix: 'drinkit', style: 'camelCase' },
      ],
      '@angular-eslint/component-selector': [
        'error',
        { type: 'element', prefix: 'drinkit', style: 'kebab-case' },
      ],

      // CLAUDE.md: no new NgModule.
      '@angular-eslint/prefer-standalone': 'error',

      // A lifecycle hook without its interface silently stops being called
      // when the method name is misspelled.
      '@angular-eslint/use-lifecycle-interface': 'error',

      // CLAUDE.md: no any. The recommended set only warns.
      '@typescript-eslint/no-explicit-any': 'error',

      // Type-only imports are erased at compile time instead of pulling the
      // module into the bundle. On a PWA opened from a QR over mobile data,
      // that is measured in seconds of waiting.
      '@typescript-eslint/consistent-type-imports': 'error',
    },
  },
  {
    files: ['**/*.html'],
    extends: [angular.configs.templateRecommended, angular.configs.templateAccessibility],
    rules: {
      // CLAUDE.md: new control flow only. Bans *ngIf and *ngFor.
      '@angular-eslint/template/prefer-control-flow': 'error',
    },
  },
]);
