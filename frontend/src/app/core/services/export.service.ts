import { Injectable } from '@angular/core';

/** Marca de ordem de byte: o Excel precisa dela para reconhecer UTF-8. */
const BOM = '﻿';

export interface ExportColumn<T> {
  header: string;
  value: (row: T) => string | number | null | undefined;
}

/**
 * Exportação tabular para planilha.
 *
 * O Excel em português abre CSV assumindo `;` como separador e espera BOM para
 * reconhecer UTF-8; sem os dois, acentuação quebra e tudo cai numa coluna só.
 */
@Injectable({ providedIn: 'root' })
export class ExportService {
  private readonly separator = ';';

  toCsv<T>(rows: readonly T[], columns: readonly ExportColumn<T>[], fileName: string): void {
    const header = columns.map((column) => this.escape(column.header)).join(this.separator);
    const body = rows.map((row) =>
      columns.map((column) => this.escape(column.value(row))).join(this.separator),
    );

    const content = [header, ...body].join('\r\n');
    // O BOM precisa ser o primeiro byte do arquivo.
    const blob = new Blob([BOM + content], { type: 'text/csv;charset=utf-8;' });
    this.download(blob, fileName.endsWith('.csv') ? fileName : `${fileName}.csv`);
  }

  private escape(value: string | number | null | undefined): string {
    if (value === null || value === undefined) return '';
    const text = String(value);
    // Aspas duplicadas e envelopadas quando há separador, aspas ou quebra.
    return /["\r\n;]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
  }

  private download(blob: Blob, fileName: string): void {
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = fileName;
    anchor.click();
    URL.revokeObjectURL(url);
  }
}
