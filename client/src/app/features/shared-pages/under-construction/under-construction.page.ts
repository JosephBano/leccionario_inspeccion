import { ChangeDetectionStrategy, Component } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';

@Component({
  selector: 'app-under-construction-page',
  imports: [MatIconModule],
  template: `
    <section class="empty">
      <mat-icon aria-hidden="true">construction</mat-icon>
      <h2>En construcción</h2>
      <p>Esta vista forma parte del roadmap y aún no está disponible.</p>
    </section>
  `,
  styles: [`
    .empty {
      display: flex; flex-direction: column; align-items: center; gap: 12px;
      padding: 48px 16px; text-align: center;
    }
    mat-icon { font-size: 48px; height: 48px; width: 48px; color: #ff8f00; }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UnderConstructionPage {}
