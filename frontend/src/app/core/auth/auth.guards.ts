import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { SessionService } from './session';
function guard(kind: 'workspace' | 'onboarding' | 'guest'): CanActivateFn {
  return async () => {
    const session = inject(SessionService);
    const router = inject(Router);
    try {
      await session.load();
    } catch {
      return router.parseUrl('/session-unavailable');
    }
    const destination = session.destination();
    if (kind === 'guest') return !session.authenticated() || router.parseUrl(destination);
    if (kind === 'onboarding') return destination === '/onboarding' || router.parseUrl(destination);
    return destination === '/' || router.parseUrl(destination);
  };
}
export const workspaceGuard = guard('workspace');
export const onboardingGuard = guard('onboarding');
export const guestGuard = guard('guest');
