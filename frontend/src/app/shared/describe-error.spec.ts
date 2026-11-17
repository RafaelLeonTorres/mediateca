import { HttpErrorResponse } from '@angular/common/http';

import { describeError } from './describe-error';

describe('describeError', () => {
  it('junta los errores de validación de la API', () => {
    const error = new HttpErrorResponse({
      status: 400,
      error: {
        title: 'One or more validation errors occurred.',
        errors: { password: ['La contraseña debe tener al menos 8 caracteres.'] },
      },
    });

    expect(describeError(error)).toBe('La contraseña debe tener al menos 8 caracteres.');
  });

  it('usa el título del problema', () => {
    const error = new HttpErrorResponse({
      status: 401,
      error: { title: 'Usuario o contraseña incorrectos.' },
    });

    expect(describeError(error)).toBe('Usuario o contraseña incorrectos.');
  });

  it('avisa cuando no hay conexión o hay demasiados intentos', () => {
    expect(describeError(new HttpErrorResponse({ status: 0 }))).toBe('No hay conexión con la API.');
    expect(describeError(new HttpErrorResponse({ status: 429 }))).toContain('Demasiados intentos');
  });
});
