import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import type { Observable } from 'rxjs';
import { tap } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface SignInResponse {
  token: string;
  userName: string;
  displayName: string;
  expiresAt: string;
}

export interface CurrentUser {
  userName: string;
  displayName: string;
}

const STORAGE_KEY = 'notaflow.session';

/**
 * Sessão do operador. O token fica em `localStorage` porque esta é uma
 * aplicação demonstrativa local; num ambiente exposto o correto seria cookie
 * `HttpOnly` emitido pelo servidor.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.billingApiUrl}/auth`;

  readonly user = signal<CurrentUser | null>(null);

  constructor() {
    const stored = this.read();
    if (stored) this.user.set({ userName: stored.userName, displayName: stored.displayName });
  }

  get token(): string | null {
    const stored = this.read();
    if (!stored) return null;
    if (new Date(stored.expiresAt).getTime() <= Date.now()) {
      this.clear();
      return null;
    }
    return stored.token;
  }

  get isAuthenticated(): boolean {
    return this.token !== null;
  }

  signIn(userName: string, password: string): Observable<SignInResponse> {
    return this.http.post<SignInResponse>(`${this.baseUrl}/login`, { userName, password }).pipe(
      tap((session) => {
        localStorage.setItem(STORAGE_KEY, JSON.stringify(session));
        this.user.set({ userName: session.userName, displayName: session.displayName });
      }),
    );
  }

  signOut(): void {
    // Invalida no servidor sem bloquear a saída da interface.
    this.http.post(`${this.baseUrl}/logout`, {}).subscribe({ error: () => undefined });
    this.clear();
  }

  private clear(): void {
    localStorage.removeItem(STORAGE_KEY);
    this.user.set(null);
  }

  private read(): SignInResponse | null {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return null;
    try {
      return JSON.parse(raw) as SignInResponse;
    } catch {
      localStorage.removeItem(STORAGE_KEY);
      return null;
    }
  }
}
