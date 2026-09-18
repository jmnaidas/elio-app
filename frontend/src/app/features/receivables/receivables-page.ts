import { Component } from '@angular/core';
import { PageHeader } from '../../shared/ui/page-header';
import { FeaturePlaceholder } from '../../shared/ui/feature-placeholder';
@Component({
  selector: 'app-receivables-page',
  imports: [PageHeader, FeaturePlaceholder],
  template: `<app-page-header
      title="Receivables"
      description="Keep the balance and the next action in view."
    /><app-feature-placeholder
      title="Clarity beyond the due date"
      description="Payment promises, collection blockers and follow-ups will come together here."
      icon="receivables"
    />`,
})
export class ReceivablesPage {}
