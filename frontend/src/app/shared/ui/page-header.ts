import { Component, input } from '@angular/core';
@Component({
  selector: 'app-page-header',
  template: '<div class="eyebrow">WORKSPACE</div><h1>{{ title() }}</h1><p>{{ description() }}</p>',
  styles: [
    '.eyebrow { color: var(--color-muted); font-size: 11px; font-weight: 600; letter-spacing: .16em; margin-bottom: 14px; } h1 { font-size: var(--font-title); font-weight: 600; letter-spacing: -.04em; } p { color: var(--color-muted); margin-top: 12px; max-width: 580px; font-size: 15px; }',
  ],
})
export class PageHeader {
  readonly title = input.required<string>();
  readonly description = input.required<string>();
}
