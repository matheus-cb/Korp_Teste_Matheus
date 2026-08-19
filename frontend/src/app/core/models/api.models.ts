export interface PagedResponse<T> {
  items: T[];
  totalCount?: number;
  total?: number;
  page?: number;
  pageNumber?: number;
  pageSize?: number;
}

export interface PageResult<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  code?: string;
  traceId?: string;
  errors?: Record<string, string[]>;
}

export interface UiError {
  title: string;
  message: string;
  status: number;
  code?: string;
  traceId?: string;
}
