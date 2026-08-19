import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import type { AiDraft } from '../models/ai-draft.model';

@Injectable({ providedIn: 'root' })
export class AiDraftService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.billingApiUrl}/invoices/ai-draft`;

  create(instruction: string, image?: File): Observable<AiDraft> {
    const data = new FormData();
    if (instruction.trim()) data.append('text', instruction.trim());
    if (image) data.append('image', image, image.name);
    return this.http.post<AiDraft>(this.url, data);
  }
}
