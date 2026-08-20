import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { map, type Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import type { PageResult, PagedResponse } from '../models/api.models';
import type { CreateProductRequest, Product, UpdateProductRequest } from '../models/product.model';

@Injectable({ providedIn: 'root' })
export class ProductService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.inventoryApiUrl}/products`;
  private readonly commandUrl = `${environment.billingApiUrl}/catalog/products`;

  list(query = '', page = 1, pageSize = 50): Observable<PageResult<Product>> {
    const params = new HttpParams()
      .set('query', query)
      .set('page', page)
      .set('pageSize', pageSize);

    return this.http
      .get<Product[] | PagedResponse<Product>>(this.baseUrl, { params })
      .pipe(map((response) => this.normalizePage(response, page, pageSize)));
  }

  getById(id: string): Observable<Product> {
    return this.http.get<Product>(`${this.baseUrl}/${encodeURIComponent(id)}`);
  }

  create(request: CreateProductRequest): Observable<Product> {
    return this.http.post<Product>(this.commandUrl, request);
  }

  update(id: string, request: UpdateProductRequest, version: string): Observable<Product> {
    return this.http.put<Product>(`${this.commandUrl}/${encodeURIComponent(id)}`, request, {
      headers: { 'If-Match': `"${version}"` },
    });
  }

  private normalizePage(
    response: Product[] | PagedResponse<Product>,
    page: number,
    pageSize: number,
  ): PageResult<Product> {
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
