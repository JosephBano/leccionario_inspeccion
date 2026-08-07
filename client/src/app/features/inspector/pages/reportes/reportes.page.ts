import { ChangeDetectionStrategy, Component } from '@angular/core';
import { UnderConstructionPage } from '@features/shared-pages/under-construction/under-construction.page';

@Component({
  selector: 'app-reportes-page',
  imports: [UnderConstructionPage],
  template: `<app-under-construction-page />`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReportesPage {}
