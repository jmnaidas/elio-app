import { Component, ElementRef, HostListener, inject, signal, viewChild } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { LineIcon } from '../../shared/ui/line-icon';

@Component({
  selector: 'app-shell',
  imports: [NgTemplateOutlet, RouterLink, RouterLinkActive, RouterOutlet, LineIcon],
  templateUrl: './app-shell.html',
  styleUrl: './app-shell.scss',
})
export class AppShell {
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
    this.drawer()?.nativeElement.close();
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
