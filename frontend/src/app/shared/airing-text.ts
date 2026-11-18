/** Días en plural con el que se dicen ("los martes", "los sábados"). */
const PLURAL: Record<string, string> = { sábado: 'sábados', domingo: 'domingos' };

/**
 * Cuándo se emite la serie, a partir de la fecha de su próximo episodio, en la hora del usuario:
 * "Se emite los martes a las 23:00". Se calcula en el navegador con `Intl`, no en el servidor.
 * Devuelve null si no hay fecha o no es válida. `timeZone` solo se pasa en las pruebas.
 */
export function describeAiring(
  nextAiringAt: string | null,
  locale = 'es',
  timeZone?: string,
): string | null {
  if (!nextAiringAt) {
    return null;
  }
  const date = new Date(nextAiringAt);
  if (Number.isNaN(date.getTime())) {
    return null;
  }

  const zone = timeZone ? { timeZone } : {};
  const weekday = new Intl.DateTimeFormat(locale, { weekday: 'long', ...zone })
    .format(date)
    .toLocaleLowerCase(locale);
  const time = new Intl.DateTimeFormat(locale, {
    hour: '2-digit',
    minute: '2-digit',
    hourCycle: 'h23',
    ...zone,
  }).format(date);

  return `Se emite los ${PLURAL[weekday] ?? weekday} a las ${time}`;
}
