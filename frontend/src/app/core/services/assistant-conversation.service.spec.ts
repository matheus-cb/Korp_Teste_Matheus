import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { AssistantConversationService } from './assistant-conversation.service';
import { AuthService } from './auth.service';
import type { CurrentUser } from './auth.service';

describe('AssistantConversationService', () => {
  const user = signal<CurrentUser | null>(null);

  function build(): AssistantConversationService {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [{ provide: AuthService, useValue: { user } }],
    });
    return TestBed.inject(AssistantConversationService);
  }

  beforeEach(() => {
    sessionStorage.clear();
    user.set({ userName: 'operador', displayName: 'Operador' });
  });

  afterEach(() => sessionStorage.clear());

  it('mantém a conversa entre instâncias do painel, que é destruído ao fechar', () => {
    const primeira = build();
    primeira.append({ role: 'user', text: 'quantas notas tenho?' });

    // Nova instância = painel reaberto, ou página recarregada.
    const segunda = build();

    expect(segunda.turns().length).toBe(1);
    expect(segunda.turns()[0].text).toBe('quantas notas tenho?');
  });

  it('não entrega a conversa de um usuário para outro na mesma aba', () => {
    const doOperador = build();
    doOperador.append({ role: 'user', text: 'saldo do cabo USB-C' });

    user.set({ userName: 'supervisor', displayName: 'Supervisor' });
    TestBed.flushEffects();

    expect(doOperador.turns()).toEqual([]);
  });

  it('devolve a conversa de cada um ao voltar o usuário', () => {
    const servico = build();
    servico.append({ role: 'user', text: 'pergunta do operador' });

    user.set({ userName: 'supervisor', displayName: 'Supervisor' });
    TestBed.flushEffects();
    servico.append({ role: 'user', text: 'pergunta do supervisor' });

    user.set({ userName: 'operador', displayName: 'Operador' });
    TestBed.flushEffects();

    expect(servico.turns().map((turn) => turn.text)).toEqual(['pergunta do operador']);
  });

  it('nunca grava o token da ação, que é credencial de escrita', () => {
    const servico = build();
    servico.append({
      role: 'assistant',
      text: 'posso criar a nota',
      action: {
        kind: 'CreateInvoice',
        items: [],
        products: [],
        expiresAt: new Date().toISOString(),
        token: 'token-secreto-de-escrita',
      },
    });

    const gravado = sessionStorage.getItem('notaflow.assistant.turns:operador') ?? '';
    expect(gravado).not.toContain('token-secreto-de-escrita');

    // E ao recarregar, a proposta se perde: a pessoa pede de novo.
    expect(build().turns()[0].action).toBeUndefined();
  });

  it('descarta conteúdo corrompido em vez de derrubar o painel', () => {
    sessionStorage.setItem('notaflow.assistant.turns:operador', 'isto não é json');

    expect(() => build()).not.toThrow();
    expect(build().turns()).toEqual([]);
  });
});
