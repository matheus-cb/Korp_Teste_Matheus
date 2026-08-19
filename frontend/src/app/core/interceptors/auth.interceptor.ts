import { inject } from '@angular/core';
import type { HttpInterceptorFn } from '@angular/common/http';
import { HttpErrorResponse } from '@angular/common/http';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthService } from '../services/auth.service';

/**
 * Anexa a sessão às chamadas da API e, se o servidor recusar, devolve o
 * operador ao login em vez de deixar a tela quebrada.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  // Assets locais (ícones) não levam credencial.
  const isApiCall = request.url.includes('/billing-api/') || request.url.includes('/inventory-api/');
  const token = auth.token;

  const authorized =
    isApiCall && token
      ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
      : request;

  return next(authorized).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status === 401 && isApiCall) {
        auth.signOut();
        void router.navigate(['/entrar']);
      }
      return throwError(() => error);
    }),
  );
};
