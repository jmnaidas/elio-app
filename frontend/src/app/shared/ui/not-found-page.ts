import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
@Component({
  selector: 'app-not-found',
  imports: [RouterLink],
  template:
    '<h1>Page not found</h1><p>This address does not exist in your workspace.</p><a routerLink="/">Return to Dashboard</a>',
})
export class NotFoundPage {}
