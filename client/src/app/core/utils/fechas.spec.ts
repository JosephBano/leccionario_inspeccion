import { describe, it, expect } from 'vitest';
import {
  fechaLocalAISO,
  lunesDe,
  domingoDe,
  hoyEnISO,
  haceDiasISO,
  sumarDiasISO,
} from './fechas';

/**
 * Tests del helper único de fechas calendario (ADR-009).
 *
 * TZ-agnósticos por construcción: armamos fechas con el constructor local
 * `new Date(yyyy, mIdx, dd, hh)` y verificamos que el resultado es el día
 * **local** esperado. Si en cualquier huso el helper se porta bien, pasa.
 *
 * El bug original era: en Ecuador (UTC-5), entre 19:00 y 23:59 hora local,
 * `lunesDe(2026-07-07)` devolvía `"2026-07-07"` (martes etiquetado como lunes).
 * El caso `a` reproduce exactamente ese escenario.
 */

describe('fechaLocalAISO', () => {
  it('devuelve componentes locales, no UTC', () => {
    // 2026-07-07 a las 20:00 hora local de Quito es 2026-07-08T01:00 UTC.
    const d = new Date(2026, 6, 7, 20, 0, 0);
    expect(fechaLocalAISO(d)).toBe('2026-07-07');
  });

  it('maneja inicio de mes', () => {
    expect(fechaLocalAISO(new Date(2026, 6, 1, 12))).toBe('2026-07-01');
  });

  it('maneja fin de año', () => {
    expect(fechaLocalAISO(new Date(2026, 11, 31, 23, 59))).toBe('2026-12-31');
  });
});

describe('lunesDe', () => {
  // Reproducción del bug original.
  it('caso a — Ecuador 20:00 martes: lunes anterior, no el día siguiente', () => {
    // 2026-07-07 martes a las 20:00 local.
    expect(lunesDe(new Date(2026, 6, 7, 20, 0, 0))).toBe('2026-07-06');
  });

  it('caso b — UTC 23:00 martes: lunes anterior', () => {
    expect(lunesDe(new Date(2026, 6, 7, 23, 0, 0))).toBe('2026-07-06');
  });

  it('caso c — mismo día martes mediodía: lunes anterior', () => {
    expect(lunesDe(new Date(2026, 6, 7, 12, 0, 0))).toBe('2026-07-06');
  });

  it('caso d — domingo: lunes ANTERIOR, no sí mismo', () => {
    // 2026-07-12 es domingo.
    expect(lunesDe(new Date(2026, 6, 12, 14, 0, 0))).toBe('2026-07-06');
  });

  it('caso d bis — lunes sigue siendo lunes', () => {
    expect(lunesDe(new Date(2026, 6, 6, 9, 0, 0))).toBe('2026-07-06');
  });

  it('acepta string yyyy-MM-dd', () => {
    expect(lunesDe('2026-07-07')).toBe('2026-07-06');
    expect(lunesDe('2026-07-12')).toBe('2026-07-06');
  });

  it('cruza fin de mes', () => {
    // 2026-08-01 es sábado.
    expect(lunesDe('2026-08-01')).toBe('2026-07-27');
  });

  it('cruza fin de año', () => {
    // 2027-01-03 es domingo.
    expect(lunesDe('2027-01-03')).toBe('2026-12-28');
  });
});

describe('domingoDe', () => {
  it('martes → domingo de esa semana', () => {
    expect(domingoDe(new Date(2026, 6, 7, 12))).toBe('2026-07-12');
  });

  it('domingo → ese mismo domingo', () => {
    expect(domingoDe(new Date(2026, 6, 12, 14))).toBe('2026-07-12');
  });

  it('lunes → domingo 6 días después', () => {
    expect(domingoDe(new Date(2026, 6, 6, 9))).toBe('2026-07-12');
  });

  it('acepta string', () => {
    expect(domingoDe('2026-07-07')).toBe('2026-07-12');
  });

  it('cruza fin de mes', () => {
    // 2026-07-27 lunes → 2026-08-02 domingo.
    expect(domingoDe('2026-07-27')).toBe('2026-08-02');
  });

  it('cruza fin de año', () => {
    // 2026-12-28 lunes → 2027-01-03 domingo.
    expect(domingoDe('2026-12-28')).toBe('2027-01-03');
  });
});

describe('hoyEnISO + haceDiasISO', () => {
  it('haceDiasISO(0) === hoyEnISO()', () => {
    expect(haceDiasISO(0)).toBe(hoyEnISO());
  });

  it('haceDiasISO(7) devuelve 7 días antes', () => {
    const hoy = hoyEnISO();
    const hace7 = haceDiasISO(7);
    expect(sumarDiasISO(hace7, 7)).toBe(hoy);
  });
});

describe('sumarDiasISO', () => {
  it('suma positivo', () => {
    expect(sumarDiasISO('2026-07-06', 6)).toBe('2026-07-12');
  });

  it('suma negativo', () => {
    expect(sumarDiasISO('2026-07-06', -7)).toBe('2026-06-29');
  });

  it('cruza mes', () => {
    expect(sumarDiasISO('2026-07-30', 3)).toBe('2026-08-02');
  });

  it('cruza año', () => {
    expect(sumarDiasISO('2026-12-28', 7)).toBe('2027-01-04');
  });

  it('cadena de semanas: +14 y luego -14 vuelve al mismo día', () => {
    const original = '2026-07-06';
    expect(sumarDiasISO(sumarDiasISO(original, 14), -14)).toBe(original);
  });
});

describe('consistencia lunes→domingo', () => {
  it('domingoDe(lunesDe(x)) === domingoDe(x)', () => {
    const casos = [
      new Date(2026, 6, 7, 12),
      new Date(2026, 6, 12, 14),
      new Date(2027, 0, 1, 10),
    ];
    for (const fecha of casos) {
      expect(domingoDe(lunesDe(fecha))).toBe(domingoDe(fecha));
    }
  });

  it('sumarDiasISO(lunesDe(x), 6) === domingoDe(x)', () => {
    const fecha = new Date(2026, 6, 7, 12);
    expect(sumarDiasISO(lunesDe(fecha), 6)).toBe(domingoDe(fecha));
  });
});