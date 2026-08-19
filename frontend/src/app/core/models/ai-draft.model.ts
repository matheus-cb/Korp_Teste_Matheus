export type DraftAvailability =
  | 'available'
  | 'insufficient'
  | 'unknown'
  | 'unavailable';

export interface AiDraftItem {
  productId: string;
  code: string;
  description: string;
  quantity: number;
  availability: DraftAvailability;
  availableBalance?: number;
}

export interface UnresolvedDraftItem {
  description: string;
  quantity?: number;
  reason: string;
  suggestions?: Array<{
    productId: string;
    code: string;
    description: string;
  }>;
}

export interface AiDraftStep {
  tool: string;
  summary: string;
  status: 'started' | 'completed' | 'failed';
}

export interface AiDraft {
  runId: string;
  items: AiDraftItem[];
  unresolvedItems: UnresolvedDraftItem[];
  warnings: string[];
  steps: AiDraftStep[];
}
