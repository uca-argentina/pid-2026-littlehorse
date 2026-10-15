import type { AbstractControl, ValidationErrors } from '@angular/forms';
import { Validators } from '@angular/forms';
import { TABLE_CAPACITY_MAX, TABLE_LOCATION_MAX_LENGTH } from './tables.service';

/**
 * Whole digits only, between 1 and the domain's limit. Judged on the trimmed
 * text and not through Number(): "5.5", "1e2" and "0x10" all parse, and none of
 * them is a table number or a head count.
 */
export function wholeBetweenOneAnd(max: number) {
  return (control: AbstractControl): ValidationErrors | null => {
    const text = typeof control.value === 'string' ? control.value.trim() : '';

    if (!/^\d+$/.test(text)) return { notANumber: true };

    const value = Number(text);

    return value >= 1 && value <= max ? null : { outOfRange: true };
  };
}

/** Counted on the trimmed text, which is what the domain keeps. */
export function locationFits(control: AbstractControl): ValidationErrors | null {
  const text = typeof control.value === 'string' ? control.value.trim() : '';

  return Validators.maxLength(TABLE_LOCATION_MAX_LENGTH)({ value: text } as AbstractControl);
}

export const CAPACITY_ERROR = `Tiene que ser un número entero, entre 1 y ${TABLE_CAPACITY_MAX} personas.`;

export const LOCATION_ERROR = `La ubicación puede tener hasta ${TABLE_LOCATION_MAX_LENGTH} caracteres.`;

/** What the API is sent: whole numbers, and no location rather than an empty one. */
export function describedAs(capacity: string, location: string) {
  return {
    capacity: Number(capacity.trim()),
    location: location.trim() === '' ? null : location.trim(),
  };
}
