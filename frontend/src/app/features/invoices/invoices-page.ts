import { Component } from '@angular/core';
import { PageHeader } from '../../shared/ui/page-header';
import { FeaturePlaceholder } from '../../shared/ui/feature-placeholder';
@Component({
  selector: 'app-invoices-page',
  imports: [PageHeader, FeaturePlaceholder],
  template: `<app-page-header
      title="Invoices"
      description="Professional billing, from the first line to the final payment."
    /><app-feature-placeholder
      title="A considered space for every invoice"
      description="Invoice creation, document previews and delivery history will live here."
      icon="invoice"
    />`,
})
export class InvoicesPage {}
