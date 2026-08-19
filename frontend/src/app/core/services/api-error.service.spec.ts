import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { ApiErrorService } from './api-error.service';

describe('ApiErrorService', () => {
  let service: ApiErrorService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(ApiErrorService);
  });

  it('traduz indisponibilidade de estoque sem expor detalhes internos', () => {
    const result = service.from(
      new HttpErrorResponse({
        status: 503,
        error: {
          code: 'INVENTORY_UNAVAILABLE',
          title: 'Service unavailable',
          traceId: 'trace-123',
        },
      }),
    );

    expect(result.message).toContain('temporariamente indisponível');
    expect(result.traceId).toBe('trace-123');
  });

  it('orienta o fluxo manual quando a IA falha', () => {
    const result = service.from(
      new HttpErrorResponse({ status: 503, error: { code: 'AI_UNAVAILABLE' } }),
    );
    expect(result.message).toContain('preenchimento manual');
  });
});
