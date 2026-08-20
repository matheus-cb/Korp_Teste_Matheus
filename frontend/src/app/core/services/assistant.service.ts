import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import type { AssistantMessage, AssistantScreen, ConfirmedAction } from '../models/assistant.model';

@Injectable({ providedIn: 'root' })
export class AssistantService {
  private readonly http = inject(HttpClient);
  private readonly base = environment.billingApiUrl;

  /**
   * Envia a mensagem com o histórico e a tela aberta, para o assistente
   * entender referências como "esse produto" ou "esta nota".
   */
  send(
    text: string,
    history: { role: string; text: string }[],
    screen: AssistantScreen | null,
  ): Observable<AssistantMessage> {
    return this.http.post<AssistantMessage>(`${this.base}/assistant/messages`, {
      text,
      history,
      screen,
    });
  }

  /**
   * Executa a ação proposta. O token assinado é a autorização: a interface não
   * decide nada sozinha, o servidor revalida tudo antes de escrever (INV-26).
   */
  confirm(token: string): Observable<ConfirmedAction> {
    return this.http.post<ConfirmedAction>(`${this.base}/assistant/actions/confirm`, { token });
  }
}
