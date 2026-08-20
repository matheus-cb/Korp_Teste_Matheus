import { Injectable, inject } from '@angular/core';
import { Router } from '@angular/router';
import type { AssistantScreen } from '../models/assistant.model';

/**
 * Traduz a rota atual para o rótulo que o assistente entende.
 *
 * O mapeamento vive aqui, e não no painel, porque quem conhece as rotas é o
 * roteador — e porque o backend só aceita rótulos de uma lista fechada: mandar
 * um valor fora dela faz o servidor descartar, não aceitar texto livre.
 */
@Injectable({ providedIn: 'root' })
export class CurrentScreenService {
  private readonly router = inject(Router);

  current(): AssistantScreen | null {
    // Sem query string nem fragmento: só o caminho interessa.
    const path = this.router.url.split(/[?#]/)[0].replace(/^\/+|\/+$/g, '');
    if (!path) return { route: 'visao-geral', entityId: null };

    const [primeiro, segundo] = path.split('/');
    switch (primeiro) {
      case 'produtos':
        return { route: 'produtos', entityId: null };
      case 'movimentacoes':
        return { route: 'movimentacoes', entityId: null };
      case 'notas':
        return segundo
          ? { route: 'nota', entityId: segundo }
          : { route: 'notas', entityId: null };
      default:
        // Rota que o assistente não conhece não vira contexto inventado.
        return null;
    }
  }
}
