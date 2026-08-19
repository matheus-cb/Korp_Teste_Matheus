import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import type { HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { catchError, from, map, switchMap, throwError, type Observable, of } from 'rxjs';
import { environment } from '../../../environments/environment';
import type { PageResult, PagedResponse } from '../models/api.models';
import type {
  CloseInvoiceResult,
  CreateInvoiceRequest,
  Invoice,
  InvoiceSummary,
  InvoiceStatus,
} from '../models/invoice.model';

@Injectable({ providedIn: 'root' })
export class InvoiceService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.billingApiUrl}/invoices`;

  list(
    status: InvoiceStatus | '' = '',
    page = 1,
    pageSize = 50,
  ): Observable<PageResult<InvoiceSummary>> {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
    if (status) params = params.set('status', status);

    return this.http
      .get<InvoiceSummary[] | PagedResponse<InvoiceSummary>>(this.baseUrl, { params })
      .pipe(map((response) => this.normalizePage(response, page, pageSize)));
  }

  getById(id: string): Observable<Invoice> {
    return this.http.get<Invoice>(`${this.baseUrl}/${encodeURIComponent(id)}`);
  }

  create(request: CreateInvoiceRequest): Observable<Invoice> {
    return this.http.post<Invoice>(this.baseUrl, request);
  }

  close(id: string): Observable<CloseInvoiceResult> {
    const url = `${this.baseUrl}/${encodeURIComponent(id)}/close`;
    return this.http
      .post<Invoice>(url, {}, { observe: 'response' })
      .pipe(
        switchMap((response) => this.normalizeCloseResponse(id, response)),
      );
  }

  downloadPdf(id: string): Observable<Blob> {
    return this.http
      .get(`${this.baseUrl}/${encodeURIComponent(id)}/pdf`, { responseType: 'blob' })
      .pipe(catchError((error: unknown) => this.normalizeBlobError(error)));
  }

  private normalizeBlobError(error: unknown): Observable<never> {
    if (!(error instanceof HttpErrorResponse) || !(error.error instanceof Blob)) {
      return throwError(() => error);
    }

    return from(error.error.text()).pipe(
      switchMap((body) => {
        let parsed: unknown = body;
        try {
          parsed = JSON.parse(body) as unknown;
        } catch {
          // Preserve non-JSON upstream errors as text.
        }
        return throwError(
          () =>
            new HttpErrorResponse({
              error: parsed,
              headers: error.headers,
              status: error.status,
              statusText: error.statusText,
              url: error.url ?? undefined,
            }),
        );
      }),
    );
  }

  private normalizeCloseResponse(
    invoiceId: string,
    response: HttpResponse<Invoice>,
  ): Observable<CloseInvoiceResult> {
    const body = response.body;
    const invoice = body ?? undefined;

    if (response.status === 202) {
      return of({
        httpStatus: 202,
        attemptId: invoice?.closure?.attemptId,
        state: 'Pending',
        message:
          invoice?.closure?.errorMessage ??
          'A baixa foi enviada e o resultado está sendo verificado.',
        invoice,
      });
    }

    if (invoice) {
      return of({ httpStatus: response.status, state: 'Completed', invoice });
    }

    return this.getById(invoiceId).pipe(
      map((loadedInvoice) => ({
        httpStatus: response.status,
        state:
          loadedInvoice.status === 'Closed' ? ('Completed' as const) : ('Pending' as const),
        invoice: loadedInvoice,
      })),
    );
  }

  private normalizePage(
    response: InvoiceSummary[] | PagedResponse<InvoiceSummary>,
    page: number,
    pageSize: number,
  ): PageResult<InvoiceSummary> {
    if (Array.isArray(response)) {
      return { items: response, total: response.length, page, pageSize };
    }
    return {
      items: response.items ?? [],
      total: response.totalCount ?? response.total ?? response.items?.length ?? 0,
      page: response.page ?? response.pageNumber ?? page,
      pageSize: response.pageSize ?? pageSize,
    };
  }
}
