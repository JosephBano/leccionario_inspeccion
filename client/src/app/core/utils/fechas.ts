/**
 * Helper único para fechas calendario del cliente.
 *
 * REGLA ABSOLUTA (ADR-009): ninguna función fuera de este archivo puede
 * llamar a `Date.prototype.toISOString()`. Si necesitás serializar una fecha
 * calendario, usá una de estas funciones. Si necesitás serializar un timestamp
 * con hora (caso raro en esta app), agregá una función acá con su test.
 *
 * CONTRATO: el resultado es siempre el día **local** del usuario, formateado
 * como `yyyy-MM-dd`. Construimos con `T00:00:00` (medianoche local) y
 * serializamos con `getFullYear` / `getMonth()+1` / `getDate`. Nunca pasamos
 * por `toISOString()`, que devuelve UTC y rompe el cálculo en husos al oeste
 * de UTC entre 19:00 y 23:59 hora local (y al este entre 00:00 y 02:59).
 */

/** Construye un `Date` anclado a medianoche local desde un `yyyy-MM-dd`. */
function parsearISO(iso: string): Date {
  // `new Date('2026-07-07')` se interpreta como UTC midnight por la spec de ES,
  // no como medianoche local. Por eso usamos el sufijo `T00:00:00` sin offset:
  // el motor lo trata como hora local.
  return new Date(`${iso}T00:00:00`);
}

/** Construye un `Date` anclado a medianoche local desde año/mes/día. */
function construirLocal(year: number, monthIdx: number, day: number): Date {
  return new Date(year, monthIdx, day, 0, 0, 0, 0);
}

/** Serializa un `Date` como `yyyy-MM-dd` usando los accesores locales. */
function formatearLocal(d: Date): string {
  const yyyy = d.getFullYear();
  const mm = String(d.getMonth() + 1).padStart(2, '0');
  const dd = String(d.getDate()).padStart(2, '0');
  return `${yyyy}-${mm}-${dd}`;
}

/** Normaliza la entrada a un `Date` anclado a su día local. */
function aDate(fecha: Date | string): Date {
  return typeof fecha === 'string' ? parsearISO(fecha) : construirLocal(
    fecha.getFullYear(),
    fecha.getMonth(),
    fecha.getDate(),
  );
}

/**
 * Convierte una `Date` a string ISO `yyyy-MM-dd` en hora LOCAL.
 * Acepta cualquier componente de hora dentro del día; los accesores locales
 * colapsan al día correcto en cualquier huso.
 */
export function fechaLocalAISO(d: Date): string {
  return formatearLocal(construirLocal(d.getFullYear(), d.getMonth(), d.getDate()));
}

/**
 * Devuelve el LUNES (local) de la semana que contiene `fecha`,
 * en formato `yyyy-MM-dd`. Acepta `Date` o string `yyyy-MM-dd`.
 *
 * Caso borde: si `fecha` cae en domingo, devuelve el lunes **anterior**,
 * no el mismo día. Una semana ISO va de lunes a domingo.
 */
export function lunesDe(fecha: Date | string): string {
  const d = aDate(fecha);
  // 0 = domingo, 1 = lunes, ..., 6 = sábado. Mapeamos a "cuántos días
  // retroceder para llegar a lunes": dom=6, lun=0, mar=1, ..., sáb=5.
  const retroceder = (d.getDay() + 6) % 7;
  d.setDate(d.getDate() - retroceder);
  return formatearLocal(d);
}

/**
 * Devuelve el DOMINGO (local) de la semana que contiene `fecha`,
 * en formato `yyyy-MM-dd`. Acepta `Date` o string `yyyy-MM-dd`.
 */
export function domingoDe(fecha: Date | string): string {
  const d = aDate(fecha);
  const avanzar = d.getDay() === 0 ? 0 : 7 - d.getDay();
  d.setDate(d.getDate() + avanzar);
  return formatearLocal(d);
}

/** Devuelve la fecha de HOY en formato `yyyy-MM-dd` local. */
export function hoyEnISO(): string {
  return fechaLocalAISO(new Date());
}

/** Devuelve la fecha de hace `dias` días (hoy − dias) en `yyyy-MM-dd` local. */
export function haceDiasISO(dias: number): string {
  const d = new Date();
  d.setDate(d.getDate() - dias);
  return formatearLocal(construirLocal(d.getFullYear(), d.getMonth(), d.getDate()));
}

/**
 * Devuelve `fecha + dias` en `yyyy-MM-dd` local. Acepta `dias` negativo.
 * Cruza mes y año correctamente porque `setDate` opera sobre el mes real.
 */
export function sumarDiasISO(fecha: string, dias: number): string {
  const d = parsearISO(fecha);
  d.setDate(d.getDate() + dias);
  return formatearLocal(d);
}