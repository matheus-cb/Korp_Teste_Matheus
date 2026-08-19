import { Injectable } from '@angular/core';
import type { AiDraftItem } from '../models/ai-draft.model';

@Injectable({ providedIn: 'root' })
export class DraftTransferService {
  private items: AiDraftItem[] = [];

  set(value: AiDraftItem[]): void {
    this.items = value.map((item) => ({ ...item }));
  }

  take(): AiDraftItem[] {
    const result = this.items;
    this.items = [];
    return result;
  }
}
