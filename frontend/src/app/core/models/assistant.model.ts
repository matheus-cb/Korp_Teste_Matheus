import type { AiDraftItem, AiDraftStep, UnresolvedDraftItem } from './ai-draft.model';

/**
 * Tela aberta quando a pessoa perguntou. O servidor normaliza contra uma lista
 * fechada — mandar daqui é conveniência, não é o que decide o que entra no
 * prompt.
 */
export interface AssistantScreen {
  route: string | null;
  entityId: string | null;
}

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
export interface ProposedProductItem {
  code: string;
  description: string;
  balance: number;
  tracksStock: boolean;
}

export interface ProposedAction {
  kind: string;
  items: ProposedActionItem[];
  /** Produtos a cadastrar; vazio quando a ação é de nota. */
  products: ProposedProductItem[];
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
  /** Número da nota; em cadastro de produto, quantos foram criados. */
  number: number;
  status: string;
  closed: boolean;
  confirmedBy: string;
}
