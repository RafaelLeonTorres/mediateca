import { describeAiring } from './airing-text';

describe('describeAiring', () => {
  // 17 de noviembre de 2026, 23:00 UTC: es martes.
  const martes = '2026-11-17T23:00:00Z';

  it('dice el día y la hora en la zona del usuario', () => {
    expect(describeAiring(martes, 'es', 'UTC')).toBe('Se emite los martes a las 23:00');
  });

  it('cambia el día y la hora según la zona horaria', () => {
    expect(describeAiring(martes, 'es', 'America/Mexico_City')).toBe(
      'Se emite los martes a las 17:00',
    );
    expect(describeAiring(martes, 'es', 'Asia/Tokyo')).toBe('Se emite los miércoles a las 08:00');
  });

  it('pone sábados y domingos en plural', () => {
    expect(describeAiring('2026-11-21T10:30:00Z', 'es', 'UTC')).toBe(
      'Se emite los sábados a las 10:30',
    );
    expect(describeAiring('2026-11-22T10:30:00Z', 'es', 'UTC')).toBe(
      'Se emite los domingos a las 10:30',
    );
  });

  it('devuelve null sin fecha o con una fecha inválida', () => {
    expect(describeAiring(null)).toBeNull();
    expect(describeAiring('no es una fecha')).toBeNull();
  });
});
