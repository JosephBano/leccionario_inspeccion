/**
 * Roles del sistema cplec. Espejo de `rbac_rol.codigo` (seed 001).
 * Doc-lineamiento: `docs/03 sección 4` y `docs/04`.
 */
export const Roles = {
  docente: 'cplec_docente',
  inspector: 'cplec_inspector',
} as const;

export type Role = (typeof Roles)[keyof typeof Roles];

export function isRole(value: string): value is Role {
  return value === Roles.docente || value === Roles.inspector;
}
