export type InvoiceStatus = 'Open' | 'Closed';
export type ClosureState = 'Pending' | 'Completed' | 'Rejected' | null;

/** Item que entrou na nota sem movimentar estoque (INV-04). */
export interface IgnoredItem {
  productId: string;
  code: string;
  quantity: number;
  reason: string;
  message: string;
}

export interface ClosureAttempt {
  attemptId: string;
  state: Exclude<ClosureState, null>;
  errorCode?: string | null;
  errorMessage?: string | null;
  retryCount: number;
  updatedAt: string;
  ignoredItems?: IgnoredItem[] | null;
}

export interface InvoiceItem {
  id?: string;
  productId: string;
  code: string;
  description: string;
  quantity: number;
}

export interface Invoice {
  id: string;
  number: number;
  status: InvoiceStatus;
  createdAt: string;
  closedAt?: string | null;
  /** Quem confirmou cada operação — rastreabilidade. */
  createdBy: string;
  closedBy?: string | null;
  items: InvoiceItem[];
  closure?: ClosureAttempt | null;
}

export interface InvoiceSummary {
  id: string;
  number: number;
  status: InvoiceStatus;
  itemCount: number;
  createdAt: string;
  closedAt?: string | null;
  createdBy: string;
  closedBy?: string | null;
  closure?: ClosureAttempt | null;
}

export interface CreateInvoiceRequest {
  items: Array<{
    productId: string;
    quantity: number;
  }>;
}

export interface CloseInvoiceResult {
  httpStatus: number;
  attemptId?: string;
  state: 'Pending' | 'Completed' | 'Rejected';
  invoice?: Invoice;
  message?: string;
}
