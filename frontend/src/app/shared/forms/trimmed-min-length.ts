import type { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';

/**
 * Validators.minLength counts the spaces, so "   " passes a minimum of three.
 * The server trims before judging, and a form that disagrees with it rejects
 * what the API would have accepted, or the other way round.
 */
export function trimmedMinLength(minimum: number): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    const value = typeof control.value === 'string' ? control.value.trim() : '';

    return value.length >= minimum ? null : { tooShort: { minimum } };
  };
}
