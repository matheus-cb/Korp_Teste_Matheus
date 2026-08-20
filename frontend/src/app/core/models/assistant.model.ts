import type { AiDraftItem, AiDraftStep, UnresolvedDraftItem } from './ai-draft.model';

export interface ProposedActionItem {
  productId: string;
  code: string;
  description: string;
  quantity: number;
}

/**
 * Ação que o assistente propõe. O `token` é assinado pelo servidor e tem prazo:
 * sem ele a execução é recusada, e é por isso que a confirmação não é apenas um
 * detalhe da interface (INV-25).
 */
export interface ProposedAction {
  kind: string;
  items: ProposedActionItem[];
  expiresAt: string;
  token: string;
}

export interface AssistantMessage {
  runId: string;
  text: string;
  items: AiDraftItem[];
  unresolvedItems: UnresolvedDraftItem[];
  warnings: string[];
  steps: AiDraftStep[];
  action: ProposedAction | null;
}

export interface ConfirmedAction {
  invoiceId: string;
  number: number;
  status: string;
  closed: boolean;
  confirmedBy: string;
}
