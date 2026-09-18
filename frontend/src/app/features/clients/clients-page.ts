import { Component } from '@angular/core';
import { PageHeader } from '../../shared/ui/page-header';
import { FeaturePlaceholder } from '../../shared/ui/feature-placeholder';
@Component({
  selector: 'app-clients-page',
  imports: [PageHeader, FeaturePlaceholder],
  template: `<app-page-header
      title="Clients"
      description="The people and businesses you work with."
    /><app-feature-placeholder
      title="Client details, kept together"
      description="Reusable billing contacts and payment preferences will have a home here."
      icon="clients"
    />`,
})
export class ClientsPage {}
