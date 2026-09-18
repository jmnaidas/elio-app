import { Component } from '@angular/core';
import { PageHeader } from '../../shared/ui/page-header';
import { FeaturePlaceholder } from '../../shared/ui/feature-placeholder';
@Component({
  selector: 'app-settings-page',
  imports: [PageHeader, FeaturePlaceholder],
  template: `<app-page-header
      title="Settings"
      description="Make the workspace work for your business."
    /><app-feature-placeholder
      title="The details behind your workspace"
      description="Organization preferences, branding and delivery settings will live here."
      icon="settings"
    />`,
})
export class SettingsPage {}
