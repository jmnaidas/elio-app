import { Component } from '@angular/core';
import { PageHeader } from '../../shared/ui/page-header';
import { FeaturePlaceholder } from '../../shared/ui/feature-placeholder';
@Component({
  selector: 'app-services-page',
  imports: [PageHeader, FeaturePlaceholder],
  template: `<app-page-header
      title="Services"
      description="Less repetition. More time for the work."
    /><app-feature-placeholder
      title="Your services, ready to reuse"
      description="Save descriptions, units and rates for future invoice creation."
      icon="services"
    />`,
})
export class ServicesPage {}
