import { type SchemaPath, minLength, required, validate } from '@angular/forms/signals';

/** Largo mínimo de una contraseña (el mismo que exige la API). */
export const MIN_PASSWORD_LENGTH = 8;

/** Tipo del error de la confirmación cuando no coincide con la contraseña. */
export const PASSWORDS_MISMATCH = 'passwordsMismatch';

/**
 * Reglas de Signal Forms para una contraseña nueva y su confirmación: la contraseña es obligatoria y tiene al
 * menos {@link MIN_PASSWORD_LENGTH} caracteres, y la confirmación tiene que coincidir con ella. El error de que no
 * coinciden queda en la confirmación, que es el campo que se marca en rojo, y se vuelve a revisar solo cuando
 * cambia cualquiera de las dos.
 */
export function newPasswordRules(password: SchemaPath<string>, confirm: SchemaPath<string>): void {
  required(password);
  minLength(password, MIN_PASSWORD_LENGTH);
  required(confirm);
  validate(confirm, ({ value, valueOf }) =>
    value() !== '' && value() !== valueOf(password) ? { kind: PASSWORDS_MISMATCH } : undefined,
  );
}
