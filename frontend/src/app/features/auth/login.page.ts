import { ChangeDetectionStrategy, Component, DestroyRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from '../../core/services/auth.service';

@Component({
  selector: 'app-login-page',
  imports: [ReactiveFormsModule],
  template: `
    <main class="screen">
      <section class="card">
        <header class="head">
          <span class="mark" aria-hidden="true">N</span>
          <div>
            <h1>NotaFlow</h1>
            <p>Estoque e faturamento em um só fluxo</p>
          </div>
        </header>

        <form [formGroup]="form" novalidate (ngSubmit)="submit()">
          <div class="nf-field" [class.invalid]="invalid('userName')">
            <label class="nf-label" for="userName">Usuário</label>
            <input
              id="userName"
              class="nf-input"
              formControlName="userName"
              autocomplete="username"
              autocapitalize="none"
              spellcheck="false"
            />
          </div>

          <div class="nf-field" [class.invalid]="invalid('password')">
            <label class="nf-label" for="password">Senha</label>
            <input
              id="password"
              class="nf-input"
              type="password"
              formControlName="password"
              autocomplete="current-password"
            />
          </div>

          @if (error) {
            <p class="error" role="alert">{{ error }}</p>
          }

          <button type="submit" class="nf-btn nf-btn--primary submit" [disabled]="loading">
            {{ loading ? 'Entrando…' : 'Entrar' }}
          </button>
        </form>

        <footer class="demo">
          <strong>Ambiente demonstrativo</strong>
          <span>operador / notaflow123</span>
          <span>supervisor / notaflow123</span>
        </footer>
      </section>
    </main>
  `,
  styles: `
    .screen {
      display: grid;
      min-height: 100dvh;
      padding: var(--sp-4);
      background: var(--n-100);
      place-items: center;
    }

    .card {
      width: 100%;
      max-width: 380px;
      overflow: hidden;
      border: 1px solid var(--n-200);
      border-radius: var(--r-md);
      background: var(--n-0);
      box-shadow: var(--shadow-md);
    }

    .head {
      display: flex;
      align-items: center;
      padding: var(--sp-5) var(--sp-5) var(--sp-4);
      border-bottom: 1px solid var(--n-200);
      gap: var(--sp-3);
    }

    .mark {
      display: grid;
      width: 34px;
      height: 34px;
      border-radius: var(--r-sm);
      color: #ffffff;
      background: var(--brand-600);
      font-size: var(--fs-lg);
      font-weight: 700;
      place-items: center;
    }

    h1 {
      margin: 0;
      color: var(--n-900);
      font-size: var(--fs-xl);
      font-weight: 660;
    }

    .head p {
      margin: 2px 0 0;
      color: var(--n-500);
      font-size: var(--fs-sm);
    }

    form {
      display: flex;
      flex-direction: column;
      padding: var(--sp-5);
      gap: var(--sp-4);
    }

    .submit {
      height: 36px;
      justify-content: center;
    }

    .error {
      margin: 0;
      padding: var(--sp-2) var(--sp-3);
      border-radius: var(--r-sm);
      color: var(--st-rejected-fg);
      background: var(--st-rejected-bg);
      font-size: var(--fs-sm);
    }

    .demo {
      display: flex;
      flex-direction: column;
      padding: var(--sp-3) var(--sp-5) var(--sp-5);
      border-top: 1px solid var(--n-100);
      color: var(--n-500);
      font-size: var(--fs-sm);
      gap: 2px;
    }

    .demo strong {
      color: var(--n-600);
      font-size: var(--fs-xs);
      font-weight: 660;
      letter-spacing: 0.06em;
      text-transform: uppercase;
    }
  `,
  changeDetection: ChangeDetectionStrategy.Default,
})
export class LoginPage {
  private readonly formBuilder = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly form = this.formBuilder.nonNullable.group({
    userName: ['', [Validators.required, Validators.minLength(3)]],
    password: ['', [Validators.required, Validators.minLength(6)]],
  });

  loading = false;
  error = '';

  invalid(name: 'userName' | 'password'): boolean {
    const control = this.form.controls[name];
    return control.invalid && (control.touched || control.dirty);
  }

  submit(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid || this.loading) return;

    const { userName, password } = this.form.getRawValue();
    this.loading = true;
    this.error = '';
    this.auth
      .signIn(userName, password)
      .pipe(
        finalize(() => (this.loading = false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => void this.router.navigate(['/']),
        error: () => (this.error = 'Usuário ou senha inválidos.'),
      });
  }
}
