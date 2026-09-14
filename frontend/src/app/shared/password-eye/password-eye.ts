import { Component, computed, input, model } from '@angular/core';

/**
 * The eye that reveals a password field. It owns the button, the icon and what
 * to call it; the screen around it keeps the input and binds its type, because
 * every password field has its own label, placeholder and autocomplete.
 *
 * It positions itself over the right-hand end of whatever wraps it, so that
 * wrapper only has to be relative and leave room on the right.
 */
@Component({
  selector: 'drinkit-password-eye',
  styleUrl: './password-eye.scss',
  templateUrl: './password-eye.html',
})
export class PasswordEye {
  /** Two-way: the screen needs it too, to choose the input's type. */
  readonly visible = model.required<boolean>();

  /** True while the form is sending, so the eye dies with the rest of it. */
  readonly disabled = input(false);

  /**
   * What the next press will do, not what the last one did. Somebody using a
   * screen reader cannot see whether the dots are there to work it out.
   */
  protected readonly label = computed(() =>
    this.visible() ? 'Ocultar la contraseña' : 'Mostrar la contraseña',
  );

  protected toggle(): void {
    this.visible.update((visible) => !visible);
  }
}
