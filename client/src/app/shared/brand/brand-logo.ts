import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * Reconstruye el isotipo ISTPET siguiendo el Manual de Identidad Corporativa
 * (retícula sección 1, tipografía sección 8-9): "IST/PET" en dorado + "Tecnológico Traversari"
 * en el color de contraste del fondo, separados por la barra vertical de marca.
 */
@Component({
  selector: 'app-brand-logo',
  template: `
    <div class="brand-logo" [class.on-dark]="onDark()">
      <div class="mark">
        <span>IST</span>
        <span>PET</span>
      </div>
      <span class="divider" aria-hidden="true"></span>
      <div class="name">
        <span>Tecnológico</span>
        <span>Traversari</span>
      </div>
      @if (tagline()) {
        <span class="tagline">Excelencia académica</span>
      }
    </div>
  `,
  styles: [`
    .brand-logo {
      display: inline-grid;
      grid-template-columns: auto auto 1fr;
      align-items: center;
      column-gap: 10px;
      row-gap: 0;
      font-family: var(--cplec-font-display);
      text-transform: uppercase;
      line-height: 0.92;
    }
    .mark {
      display: flex;
      flex-direction: column;
      font-weight: 700;
      font-size: 1.05rem;
      letter-spacing: 0.5px;
      color: var(--cplec-gold);
    }
    .divider {
      align-self: stretch;
      width: 2px;
      background: var(--cplec-gold);
      border-radius: 1px;
    }
    .name {
      display: flex;
      flex-direction: column;
      font-weight: 600;
      font-size: 0.92rem;
      letter-spacing: 0.4px;
      color: var(--cplec-navy);
    }
    .brand-logo.on-dark .name { color: #fff; }
    .tagline {
      grid-column: 1 / -1;
      font-family: var(--cplec-font-script);
      text-transform: none;
      font-size: 1rem;
      color: var(--cplec-gold);
      margin-top: 2px;
      letter-spacing: 0.3px;
    }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BrandLogo {
  readonly onDark = input(false);
  readonly tagline = input(false);
}
