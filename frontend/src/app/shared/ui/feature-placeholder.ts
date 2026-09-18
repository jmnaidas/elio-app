import { Component, input } from '@angular/core';
import { LineIcon } from './line-icon';
@Component({
  selector: 'app-feature-placeholder',
  imports: [LineIcon],
  template:
    '<section class="placeholder" [attr.aria-label]="title()"><div class="icon"><app-icon [name]="icon()" /></div><span class="stage">PHASE 0 · FOUNDATION</span><h2>{{ title() }}</h2><p>{{ description() }}</p><div class="footnote">This workspace is taking shape. Business features will arrive in a later phase.</div></section>',
  styles: [
    '.placeholder { margin-top: 36px; min-height: 340px; padding: 56px 28px 32px; display: flex; align-items: center; flex-direction: column; text-align: center; background: var(--color-surface); border: var(--border-subtle); border-radius: var(--radius-md); box-shadow: var(--shadow-soft); } .icon { display:flex; padding:14px; background:var(--color-subtle); border-radius:12px; color:var(--color-primary); margin-bottom:22px; } .stage { font-size:10px; letter-spacing:.14em; color:var(--color-muted); font-weight:600; } h2 { margin-top:12px; font-size:20px; font-weight:600; letter-spacing:-.02em; } p { margin-top:12px; max-width:430px; color:var(--color-muted); } .footnote { margin-top:38px; border-top:var(--border-subtle); padding-top:20px; font-size:12px; color:var(--color-muted); width:100%; }',
  ],
})
export class FeaturePlaceholder {
  readonly title = input.required<string>();
  readonly description = input.required<string>();
  readonly icon = input('grid');
}
