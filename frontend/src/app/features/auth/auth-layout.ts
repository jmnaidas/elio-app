import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
@Component({
  selector: 'app-auth-layout',
  imports: [RouterLink],
  template: `<main class="auth-layout">
    <div class="auth-brand">
      <a routerLink="/login" aria-label="ELIO home">elio<span>.</span></a>
      <p>Know what's due. Know what's next.</p>
    </div>
    <section class="auth-panel"><ng-content /></section>
    <footer>Exceptions · Ledger · Invoicing · Operations</footer>
  </main>`,
  styles: `
    .auth-layout {
      min-height: 100dvh;
      padding: 48px 24px 28px;
      display: flex;
      flex-direction: column;
      align-items: center;
      background: var(--color-canvas);
    }
    .auth-brand {
      width: 100%;
      max-width: 420px;
      margin-bottom: 32px;
    }
    .auth-brand a {
      color: var(--color-primary);
      font-size: 38px;
      font-weight: 750;
      letter-spacing: -2px;
      text-decoration: none;
    }
    .auth-brand a span {
      color: var(--color-text);
    }
    .auth-brand p {
      color: var(--color-muted);
      margin-top: 4px;
    }
    .auth-panel {
      width: 100%;
      max-width: 420px;
      padding: 32px;
      border: 1px solid var(--color-border);
      border-radius: 12px;
      background: var(--color-surface);
    }
    footer {
      margin-top: 32px;
      color: var(--color-muted);
      font-size: 11px;
      text-align: center;
    }
    @media (max-width: 480px) {
      .auth-layout {
        padding: 28px 16px;
      }
      .auth-panel {
        padding: 24px 20px;
      }
      .auth-brand {
        margin-bottom: 24px;
      }
    }
  `,
})
export class AuthLayout {}
