import { Component, ElementRef, HostListener, inject, signal, viewChild } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { LineIcon } from '../../shared/ui/line-icon';
import { SessionService, errorMessage } from '../auth/session';

@Component({
  selector: 'app-shell',
  imports: [NgTemplateOutlet, RouterLink, RouterLinkActive, RouterOutlet, LineIcon],
  templateUrl: './app-shell.html',
  styleUrl: './app-shell.scss',
})
export class AppShell {
  readonly session = inject(SessionService);
  private readonly router = inject(Router);
  readonly loggingOut = signal(false);
  readonly logoutError = signal('');
  async logout() {
    this.loggingOut.set(true);
    this.logoutError.set('');
    try {
      await this.session.logout();
      await this.router.navigateByUrl('/login');
    } catch (error) {
      this.logoutError.set(errorMessage(error));
    } finally {
      this.loggingOut.set(false);
    }
  }
  readonly collapsed = signal(false);
  readonly menuOpen = signal(false);
  private readonly drawer = viewChild<ElementRef<HTMLDialogElement>>('drawer');
  private readonly workspace = viewChild<ElementRef<HTMLElement>>('workspace');
  readonly navigation = [
    { label: 'Dashboard', path: '/', icon: 'grid' },
    { label: 'Invoices', path: '/invoices', icon: 'invoice' },
    { label: 'Receivables', path: '/receivables', icon: 'receivables' },
    { label: 'Clients', path: '/clients', icon: 'clients' },
    { label: 'Services', path: '/services', icon: 'services' },
  ];
  constructor() {
    inject(Router)
      .events.pipe(takeUntilDestroyed())
      .subscribe((event) => {
        if (event instanceof NavigationEnd) {
          this.closeMenu();
          this.workspace()?.nativeElement.focus();
        }
      });
  }
  openMenu() {
    this.drawer()?.nativeElement.showModal();
    this.menuOpen.set(true);
  }
  closeMenu() {
    const drawer = this.drawer()?.nativeElement;
    if (drawer?.open) drawer.close();
    this.menuOpen.set(false);
  }
  backdropClick(event: MouseEvent) {
    if (event.target === this.drawer()?.nativeElement) this.closeMenu();
  }
  @HostListener('window:resize')
  onResize() {
    if (window.matchMedia('(min-width: 960px)').matches) this.closeMenu();
  }
}
