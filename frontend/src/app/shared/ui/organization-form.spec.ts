import { TestBed } from '@angular/core/testing';
import { OrganizationForm } from './organization-form';
describe('Organization settings form', () => {
  it('loads stored organization settings and submits the concurrency version', async () => {
    const fixture = TestBed.createComponent(OrganizationForm);
    fixture.componentRef.setInput('organization', {
      id: 'org',
      name: 'Studio',
      timeZone: 'America/New_York',
      defaultCurrency: 'USD',
      version: 'original-version',
    });
    await fixture.whenStable();
    const saved = vi.fn();
    fixture.componentInstance.saved.subscribe(saved);
    fixture.componentInstance.form.controls.name.setValue('Updated Studio');
    fixture.componentInstance.submit();
    expect(saved).toHaveBeenCalledWith({
      name: 'Updated Studio',
      timeZone: 'America/New_York',
      defaultCurrency: 'USD',
      version: 'original-version',
    });
  });
  it('rejects an empty name and prevents duplicate submissions while busy', async () => {
    const fixture = TestBed.createComponent(OrganizationForm);
    const saved = vi.fn();
    fixture.componentInstance.saved.subscribe(saved);
    fixture.componentInstance.submit();
    expect(saved).not.toHaveBeenCalled();
    fixture.componentInstance.form.controls.name.setValue('Studio');
    fixture.componentRef.setInput('busy', true);
    await fixture.whenStable();
    fixture.componentInstance.submit();
    expect(saved).not.toHaveBeenCalled();
  });
});
