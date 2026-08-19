import { inject, provideAppInitializer } from '@angular/core';
import type { EnvironmentProviders } from '@angular/core';
import { MatIconRegistry } from '@angular/material/icon';
import { DomSanitizer } from '@angular/platform-browser';

/**
 * Ícones do Lucide (ISC), versionados em `public/icons` junto da licença.
 * O resolvedor evita manter uma lista de nomes: `<mat-icon svgIcon="search">`
 * carrega `icons/search.svg` sob demanda.
 */
const ICON_NAME = /^[a-z0-9]+(-[a-z0-9]+)*$/;

export function provideNotaFlowIcons(): EnvironmentProviders {
  return provideAppInitializer(() => {
    const registry = inject(MatIconRegistry);
    const sanitizer = inject(DomSanitizer);

    registry.addSvgIconResolver((name) =>
      // O nome vira parte de uma URL marcada como confiável, então só
      // aceitamos o formato dos arquivos que versionamos.
      ICON_NAME.test(name)
        ? sanitizer.bypassSecurityTrustResourceUrl(`icons/${name}.svg`)
        : null,
    );
  });
}
