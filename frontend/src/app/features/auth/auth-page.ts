import { Component, computed, inject, signal } from '@angular/core';
import { Location } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { AuthLayout } from './auth-layout';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { errorMessage, SessionService } from '../../core/auth/session';
type Mode = 'login' | 'register' | 'forgot-password' | 'reset-password' | 'verify-email';

function parseAccountLink(fragment: string | null) {
  const parameters = new URLSearchParams(fragment ?? '');
  const userId = parameters.get('userId');
  const token = parameters.get('token');
  return userId && token ? { userId, token } : null;
}
@Component({
  selector: 'app-auth-page',
  imports: [AuthLayout, ReactiveFormsModule, RouterLink],
  template: ` <app-auth-layout>
    <p class="eyebrow">YOUR ELIO ACCOUNT</p>
    <h1>{{ titles[mode] }}</h1>
    <p class="form-intro">{{ descriptions[mode] }}</p>
    @if (message()) {
      <p class="notice" role="status">{{ message() }}</p>
    }
    @if (error()) {
      <p class="form-error" role="alert">{{ error() }}</p>
    }
    @if (!done()) {
      <form [formGroup]="form" (ngSubmit)="submit()" [attr.aria-busy]="busy()" class="elio-form">
        @if (showEmail) {
          <label for="email">Email address</label
          ><input
            id="email"
            type="email"
            formControlName="email"
            autocomplete="email"
            maxlength="254"
            required
          />
          @if (form.controls.email.touched && form.controls.email.invalid) {
            <p class="field-error">Enter a valid email address.</p>
          }
        }
        @if (showPassword) {
          <label for="password">{{
            mode === 'reset-password' ? 'New password' : 'Password'
          }}</label>
          <input
            id="password"
            type="password"
            formControlName="password"
            [autocomplete]="mode === 'login' ? 'current-password' : 'new-password'"
            maxlength="128"
            required
            [attr.aria-describedby]="mode !== 'login' ? 'password-help' : null"
          />
          @if (mode !== 'login') {
            <p id="password-help" class="field-help">
              Use 12–128 characters with uppercase, lowercase, a number and a symbol.
            </p>
          }
          @if (confirm) {
            <label for="confirm">Confirm password</label
            ><input
              id="confirm"
              type="password"
              formControlName="confirmPassword"
              autocomplete="new-password"
              maxlength="128"
              required
            />
          }
          @if (form.touched && form.hasError('mismatch')) {
            <p class="field-error">Passwords must match.</p>
          }
        }
        <button class="primary-button" type="submit" [disabled]="busy()">
          {{ busy() ? 'Please wait…' : action }}
        </button>
      </form>
    }
    @if (mode === 'login') {
      <div class="auth-links">
        <a routerLink="/forgot-password">Forgot password?</a
        ><span>New to ELIO? <a routerLink="/register">Create an account</a></span>
      </div>
    } @else if (mode === 'verify-email' && session.authenticated()) {
      <div class="auth-links">
        <button class="text-button" type="button" [disabled]="busy()" (click)="continue()">
          I've verified my email — continue</button
        ><button class="text-button" type="button" [disabled]="busy()" (click)="logout()">
          Sign out
        </button>
      </div>
    } @else {
      <p class="auth-links"><a routerLink="/login">Back to sign in</a></p>
    }
    @if (mode === 'verify-email' && token() && !done()) {
      <p class="auth-links">
        <button type="button" class="text-button" (click)="resendMode()">
          Need a new verification link?
        </button>
      </p>
    }
  </app-auth-layout>`,
})
export class AuthPage {
  readonly session = inject(SessionService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  readonly mode = this.route.snapshot.data['mode'] as Mode;
  readonly titles: Record<Mode, string> = {
    login: 'Welcome back',
    register: 'Create your account',
    'forgot-password': 'Reset your password',
    'reset-password': 'Choose a new password',
    'verify-email': 'Verify your email',
  };
  readonly descriptions: Record<Mode, string> = {
    login: 'Sign in to your workspace.',
    register: 'Start with your account. Set up your business next.',
    'forgot-password':
      'Enter your email and we’ll send you a reset link if your account is eligible.',
    'reset-password': 'Choose a strong password to secure your account.',
    'verify-email':
      'Confirm your email before setting up your workspace. Check your inbox for a verification link.',
  };
  // Fragment-only navigation reuses this component. Signal updates notify its view as well.
  private readonly accountLink = signal(parseAccountLink(this.route.snapshot.fragment));
  readonly token = computed(() => this.accountLink()?.token ?? '');
  private get userId() {
    return this.accountLink()?.userId ?? '';
  }
  readonly busy = signal(false);
  readonly message = signal('');
  readonly error = signal('');
  readonly done = signal(false);
  readonly form = inject(FormBuilder).nonNullable.group(
    {
      email: [
        this.session.pendingEmail() || this.session.user()?.email || '',
        this.showEmail ? [Validators.required, Validators.email] : [],
      ],
      password: [
        '',
        this.showPassword
          ? [
              Validators.required,
              Validators.maxLength(128),
              ...(this.confirm ? [Validators.minLength(12)] : []),
            ]
          : [],
      ],
      confirmPassword: ['', this.confirm ? [Validators.required] : []],
    },
    {
      validators: (group) =>
        this.confirm && group.get('password')?.value !== group.get('confirmPassword')?.value
          ? { mismatch: true }
          : null,
    },
  );
  constructor() {
    const location = inject(Location);
    this.route.fragment.pipe(takeUntilDestroyed()).subscribe((fragment) => {
      if (!fragment) return;
      this.accountLink.set(parseAccountLink(fragment));
      this.done.set(false);
      this.error.set('');
      this.message.set('');
      this.form.controls.email.setValidators(
        this.showEmail ? [Validators.required, Validators.email] : [],
      );
      this.form.controls.email.updateValueAndValidity();
      location.replaceState(`/${this.mode}`);
    });
    void this.session.load().catch((error) => this.error.set(errorMessage(error)));
    if (this.mode === 'reset-password' && (!this.token() || !this.userId)) {
      this.error.set('Open a valid password reset link from your email.');
      this.done.set(true);
    }
  }
  get showEmail() {
    return (
      this.mode === 'login' ||
      this.mode === 'register' ||
      this.mode === 'forgot-password' ||
      (this.mode === 'verify-email' && !this.token())
    );
  }
  get showPassword() {
    return this.mode === 'login' || this.mode === 'register' || this.mode === 'reset-password';
  }
  get confirm() {
    return this.mode === 'register' || this.mode === 'reset-password';
  }
  get action() {
    return this.mode === 'login'
      ? 'Sign in'
      : this.mode === 'register'
        ? 'Create account'
        : this.mode === 'forgot-password'
          ? 'Send reset link'
          : this.mode === 'reset-password'
            ? 'Save new password'
            : this.token()
              ? 'Verify email'
              : 'Send verification link';
  }
  resendMode() {
    this.accountLink.set(null);
    this.done.set(false);
    this.error.set('');
    this.form.controls.email.setValidators([Validators.required, Validators.email]);
    this.form.controls.email.updateValueAndValidity();
  }
  async submit() {
    this.form.markAllAsTouched();
    if (this.form.invalid || this.busy()) {
      this.error.set('Please check the form fields and password requirements.');
      return;
    }
    this.busy.set(true);
    this.error.set('');
    this.message.set('');
    const value = this.form.getRawValue();
    try {
      if (this.mode === 'login') {
        await this.session.login(value.email, value.password);
        await this.router.navigateByUrl(this.session.destination());
      } else if (this.mode === 'register') {
        await this.session.mutate('/auth/register', value);
        this.session.pendingEmail.set(value.email);
        await this.router.navigateByUrl('/verify-email');
      } else if (this.mode === 'verify-email' && this.token()) {
        await this.session.mutate('/auth/verify-email', { userId: this.userId, token: this.token() });
        await this.session.refresh();
        this.done.set(true);
        this.message.set('Email verified. You can now continue to your workspace or sign in.');
      } else if (this.mode === 'reset-password') {
        await this.session.mutate('/auth/reset-password', {
          ...value,
          userId: this.userId,
          token: this.token(),
        });
        await this.session.refresh();
        this.done.set(true);
        this.message.set('Password updated. Sign in with your new password.');
      } else {
        await this.session.mutate(
          this.mode === 'verify-email' ? '/auth/resend-verification' : '/auth/forgot-password',
          { email: value.email },
        );
        this.message.set(
          'If your account is eligible, an email with the next steps will arrive shortly.',
        );
      }
    } catch (error) {
      this.error.set(errorMessage(error));
    } finally {
      this.busy.set(false);
    }
  }
  async continue() {
    this.busy.set(true);
    this.error.set('');
    try {
      await this.session.refresh();
      if (!this.session.user()?.emailVerified)
        this.message.set('Your email is not verified yet. Open the link in your email first.');
      else await this.router.navigateByUrl(this.session.destination());
    } catch (error) {
      this.error.set(errorMessage(error));
    } finally {
      this.busy.set(false);
    }
  }
  async logout() {
    this.busy.set(true);
    try {
      await this.session.logout();
      await this.router.navigateByUrl('/login');
    } catch (error) {
      this.error.set(errorMessage(error));
    } finally {
      this.busy.set(false);
    }
  }
}
