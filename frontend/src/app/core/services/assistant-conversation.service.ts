import { Injectable, signal } from '@angular/core';
import type { AiDraft } from '../models/ai-draft.model';
import type { ProposedAction } from '../models/assistant.model';

export interface AssistantTurn {
  role: 'user' | 'assistant';
  text: string;
  draft?: AiDraft;
  /**
   * Ação assinada pelo servidor. Fica só em memória: é credencial de escrita
   * com prazo, e não tem por que sobreviver a um F5 (ver `persist`).
   */
  action?: ProposedAction;
  /** Verdadeiro depois que a pessoa confirmou e a nota nasceu. */
  done?: boolean;
}

const STORAGE_KEY = 'notaflow.assistant.turns';
const MAX_STORED_TURNS = 40;

/**
 * Estado da conversa com o assistente.
 *
 * Vive num serviço, e não no componente, porque o painel é montado sob
 * `@if (agentOpen)` no shell — ele é destruído e recriado ao fechar, e o
 * histórico ia junto. Sendo `providedIn: 'root'`, sobrevive a isso e à troca
 * de rota.
 *
 * O espelho em `sessionStorage` estende a sobrevida ao F5, e termina quando a
 * aba fecha. O token da ação proposta é deliberadamente deixado de fora.
 */
@Injectable({ providedIn: 'root' })
export class AssistantConversationService {
  private readonly state = signal<AssistantTurn[]>(this.restore());

  readonly turns = this.state.asReadonly();

  append(turn: AssistantTurn): void {
    this.state.update((turns) => [...turns, turn]);
    this.persist();
  }

  /** Substitui um turno específico — usado ao confirmar a criação da nota. */
  replace(target: AssistantTurn, replacement: AssistantTurn): void {
    this.state.update((turns) => turns.map((turn) => (turn === target ? replacement : turn)));
    this.persist();
  }

  remove(target: AssistantTurn): void {
    this.state.update((turns) => turns.filter((turn) => turn !== target));
    this.persist();
  }

  clear(): void {
    this.state.set([]);
    this.persist();
  }

  /** Histórico enviado ao backend para o assistente entender referências. */
  history(): { role: string; text: string }[] {
    return this.state().map((turn) => ({ role: turn.role, text: turn.text }));
  }

  private persist(): void {
    try {
      // Sem `action`: o token é credencial de escrita com validade, e disco do
      // navegador não é lugar para isso. Ao recarregar, a proposta se perde e a
      // pessoa pede de novo — que é o comportamento seguro.
      const serializable = this.state()
        .slice(-MAX_STORED_TURNS)
        .map(({ role, text, draft, done }) => ({ role, text, draft, done }));
      sessionStorage.setItem(STORAGE_KEY, JSON.stringify(serializable));
    } catch {
      // Cota estourada ou storage bloqueado: a conversa segue em memória.
    }
  }

  private restore(): AssistantTurn[] {
    try {
      const raw = sessionStorage.getItem(STORAGE_KEY);
      if (!raw) return [];
      const parsed: unknown = JSON.parse(raw);
      if (!Array.isArray(parsed)) return [];

      // Leitura defensiva: conteúdo corrompido é descartado, nunca derruba o painel.
      return parsed
        .filter(
          (turn): turn is AssistantTurn =>
            !!turn &&
            typeof turn === 'object' &&
            (turn as AssistantTurn).role !== undefined &&
            typeof (turn as AssistantTurn).text === 'string',
        )
        .map((turn) => ({ ...turn, action: undefined }));
    } catch {
      return [];
    }
  }
}
