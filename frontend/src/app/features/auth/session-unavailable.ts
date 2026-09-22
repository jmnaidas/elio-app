import { Component } from '@angular/core';
import { AuthLayout } from './auth-layout';
@Component({
  selector: 'app-session-unavailable',
  imports: [AuthLayout],
  template: `<app-auth-layout
    ><h1>Unable to connect</h1>
    <p class="form-intro">
      ELIO could not restore your session. Check your connection, then try again.
    </p>
    <a class="primary-button" href="/">Try again</a></app-auth-layout
  >`,
})
export class SessionUnavailable {}
