import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { InvoiceService } from './invoice.service';

describe('InvoiceService', () => {
  let service: InvoiceService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(InvoiceService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('preserva 202 como tentativa pendente', () => {
    service.close('invoice-1').subscribe((result) => {
      expect(result.httpStatus).toBe(202);
      expect(result.state).toBe('Pending');
      expect(result.attemptId).toBe('attempt-1');
    });

    const request = http.expectOne(
      `${environment.billingApiUrl}/invoices/invoice-1/close`,
    );
    expect(request.request.method).toBe('POST');
    request.flush(
      {
        id: 'invoice-1',
        number: 1,
        status: 'Open',
        createdAt: '2026-08-13T12:00:00Z',
        items: [],
        closure: {
          attemptId: 'attempt-1',
          state: 'Pending',
          retryCount: 1,
          updatedAt: '2026-08-13T12:00:01Z',
        },
      },
      { status: 202, statusText: 'Accepted' },
    );
  });

  it('normaliza a listagem resumida sem supor itens detalhados', () => {
    service.list('', 1, 20).subscribe((result) => {
      expect(result.items[0].itemCount).toBe(3);
      expect(result.total).toBe(1);
    });

    const request = http.expectOne(
      (candidate) =>
        candidate.url === `${environment.billingApiUrl}/invoices` &&
        candidate.params.get('page') === '1' &&
        candidate.params.get('pageSize') === '20',
    );
    request.flush({
      items: [
        {
          id: 'invoice-1',
          number: 1,
          status: 'Open',
          itemCount: 3,
          createdAt: '2026-08-13T12:00:00Z',
          closure: null,
        },
      ],
      page: 1,
      pageSize: 20,
      total: 1,
    });
  });

  it('solicita o PDF como conteúdo binário', () => {
    service.downloadPdf('invoice-1').subscribe((blob) => {
      expect(blob.type).toBe('application/pdf');
    });

    const request = http.expectOne(
      `${environment.billingApiUrl}/invoices/invoice-1/pdf`,
    );
    expect(request.request.responseType).toBe('blob');
    request.flush(new Blob(['pdf'], { type: 'application/pdf' }));
  });

  it('converte ProblemDetails recebido como Blob ao falhar o PDF', (done) => {
    service.downloadPdf('invoice-1').subscribe({
      next: () => done.fail('A requisição deveria falhar.'),
      error: (error: unknown) => {
        const body = (error as { error?: { code?: string; traceId?: string } }).error;
        expect(body?.code).toBe('INVOICE_NOT_CLOSED');
        expect(body?.traceId).toBe('trace-1');
        done();
      },
    });

    const request = http.expectOne(
      `${environment.billingApiUrl}/invoices/invoice-1/pdf`,
    );
    request.flush(
      new Blob(
        [JSON.stringify({ code: 'INVOICE_NOT_CLOSED', traceId: 'trace-1' })],
        { type: 'application/problem+json' },
      ),
      { status: 409, statusText: 'Conflict' },
    );
  });
});
