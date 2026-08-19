import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';
import { map } from 'rxjs';
import { environment } from '../../../environments/environment';
import type { PageResult, PagedResponse } from '../models/api.models';
import type { StockMovement } from '../models/movement.model';

@Injectable({ providedIn: 'root' })
export class MovementService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.inventoryApiUrl}/movements`;

  list(page = 1, pageSize = 100): Observable<PageResult<StockMovement>> {
    const params = new HttpParams().set('page', page).set('pageSize', pageSize);
    return this.http.get<PagedResponse<StockMovement>>(this.url, { params }).pipe(
      map((response) => ({
        items: response.items ?? [],
        total: response.totalCount ?? response.total ?? 0,
        page: response.page ?? page,
        pageSize: response.pageSize ?? pageSize,
      })),
    );
  }
}
