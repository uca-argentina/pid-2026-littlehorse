import { render, screen } from '@testing-library/angular';
import { PasswordEye } from './password-eye';

async function openEye(visible = false) {
  const rendered = await render(PasswordEye, { inputs: { visible } });

  return { rendered, eye: rendered.fixture.componentInstance };
}

function button(): HTMLButtonElement {
  return screen.getByRole('button') as HTMLButtonElement;
}

describe('PasswordEye', () => {
  it('asks to reveal the password while it is hidden', async () => {
    await openEye(false);

    expect(button().getAttribute('aria-label')).toBe('Mostrar la contraseña');
  });

  // The label says what the next press will do, not what the last one did:
  // somebody using a screen reader cannot see the dots to work it out.
  it('asks to hide the password while it is shown', async () => {
    await openEye(true);

    expect(button().getAttribute('aria-label')).toBe('Ocultar la contraseña');
  });

  it('flips what it is asked to show when pressed', async () => {
    const { rendered, eye } = await openEye(false);

    button().click();
    await rendered.fixture.whenStable();

    expect(eye.visible()).toBe(true);
  });

  it('flips back on the second press', async () => {
    const { rendered, eye } = await openEye(true);

    button().click();
    await rendered.fixture.whenStable();

    expect(eye.visible()).toBe(false);
  });

  // A button inside a form submits it unless it says otherwise, and this one
  // would send a half-filled form every time somebody peeked.
  it('is not a submit button', async () => {
    await openEye();

    expect(button().type).toBe('button');
  });

  it('can be locked along with the form around it', async () => {
    await render(PasswordEye, { inputs: { visible: false, disabled: true } });

    expect(button().disabled).toBe(true);
  });

  it('is usable by default, because most forms are not sending', async () => {
    await openEye();

    expect(button().disabled).toBe(false);
  });
});
