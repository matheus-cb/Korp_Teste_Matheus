import { Injectable, inject } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ToastComponent } from '../../shared/toast.component';
import type { ToastData, ToastTone } from '../../shared/toast.component';

/**
 * Ponto único de aviso ao usuário. Toda mensagem de sucesso, erro, atenção ou
 * informação passa por aqui, para que a aplicação inteira fale no mesmo tom e
 * no mesmo lugar da tela.
 */
@Injectable({ providedIn: 'root' })
export class NotificationService {
  private readonly snackBar = inject(MatSnackBar);

  /** Erro fica mais tempo: costuma trazer instrução de correção. */
  private readonly durations: Record<ToastTone, number> = {
    success: 4500,
    info: 5000,
    warning: 6000,
    error: 8000,
  };

  success(title: string, message?: string): void {
    this.show('success', title, message);
  }

  error(title: string, message?: string): void {
    this.show('error', title, message);
  }

  warning(title: string, message?: string): void {
    this.show('warning', title, message);
  }

  info(title: string, message?: string): void {
    this.show('info', title, message);
  }

  private show(tone: ToastTone, title: string, message?: string): void {
    const duration = this.durations[tone];
    const data: ToastData = { tone, title, message, duration };

    this.snackBar.openFromComponent(ToastComponent, {
      data,
      duration,
      horizontalPosition: 'end',
      verticalPosition: 'top',
      panelClass: ['nf-toast'],
    });
  }
}
