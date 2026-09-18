import { Component } from '@angular/core';
import { PageHeader } from '../../shared/ui/page-header';
import { FeaturePlaceholder } from '../../shared/ui/feature-placeholder';
@Component({
  selector: 'app-dashboard-page',
  imports: [PageHeader, FeaturePlaceholder],
  template: `<app-page-header
      title="Dashboard"
      description="A clear view of your billing, with room to focus."
    /><app-feature-placeholder
      title="Your next step, in one place"
      description="This will be your home for outstanding balances and the actions that need your attention."
      icon="grid"
    />`,
})
export class DashboardPage {}
