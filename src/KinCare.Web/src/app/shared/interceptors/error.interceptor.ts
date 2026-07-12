import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { ZodError } from 'zod';
import { AuthService } from '../auth/auth.service';

export interface ApiError {
  status: number;
  title: string;
  correlationId?: string;
  requiredTier?: string;
}

export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const router = inject(Router);
  const auth = inject(AuthService);

  return next(req).pipe(
    catchError((err: unknown) => {
      // Zod schema parse failure — API response shape doesn't match our contract
      if (err instanceof ZodError) {
        console.error('[API Contract] Response failed schema validation', {
          url: req.url,
          issues: err.issues,
        });
        return throwError(() => ({
          status: 0,
          title: 'Unexpected response format from server. Please refresh.',
          correlationId: undefined,
        } satisfies ApiError));
      }

      if (!(err instanceof HttpErrorResponse)) return throwError(() => err);

      const correlationId: string | undefined = err.error?.correlationId;
      const title: string = extractTitle(err);

      const apiError: ApiError = {
        status: err.status,
        title,
        correlationId,
        requiredTier: err.error?.extensions?.requiredTier ?? err.error?.requiredTier,
      };

      switch (err.status) {
        case 0:
          console.error('[Network] No connection to API', { url: req.url });
          break;

        case 400:
          console.warn('[API 400] Bad request', { url: req.url, title, correlationId });
          break;

        case 401:
          // JWT interceptor handles 401 with token refresh — only log here if it bubbles through
          console.warn('[API 401] Unauthorized — token refresh may have failed', { url: req.url });
          break;

        case 402:
          console.warn('[API 402] Plan gate — upgrade required', {
            url: req.url,
            requiredTier: apiError.requiredTier,
            correlationId,
          });
          router.navigate(['/billing'], {
            queryParams: { reason: 'plan', tier: apiError.requiredTier },
          });
          break;

        case 403:
          console.warn('[API 403] Forbidden', { url: req.url, correlationId });
          break;

        case 404:
          console.warn('[API 404] Not found', { url: req.url, correlationId });
          break;

        case 409:
          console.warn('[API 409] Conflict', { url: req.url, title, correlationId });
          break;

        case 422:
          console.warn('[API 422] Validation error', { url: req.url, errors: err.error?.errors, correlationId });
          break;

        case 429:
          console.warn('[API 429] Rate limited', { url: req.url });
          break;

        default:
          if (err.status >= 500) {
            console.error('[API 5xx] Server error', {
              url: req.url,
              status: err.status,
              title,
              correlationId,
            });
          }
      }

      return throwError(() => apiError);
    }),
  );
};

function extractTitle(err: HttpErrorResponse): string {
  if (typeof err.error === 'string') return err.error;
  if (err.error?.title) return err.error.title;
  if (err.error?.error) return err.error.error;

  // FluentValidation problem details — flatten field errors
  if (err.error?.errors && typeof err.error.errors === 'object') {
    const msgs = Object.values(err.error.errors).flat() as string[];
    if (msgs.length > 0) return msgs.join(' ');
  }

  return err.message || `HTTP ${err.status}`;
}
