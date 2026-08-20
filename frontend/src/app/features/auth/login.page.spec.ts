import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';
import { LoginPage } from './login.page';

describe('LoginPage', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: jasmine.createSpyObj('AuthService', ['signIn']) },
        { provide: Router, useValue: jasmine.createSpyObj('Router', ['navigate']) },
      ],
    })
      // A mudança é de estado do componente; o template inteiro é validado no
      // build, enquanto este teste evita dependência do resolvedor de SVG.
      .overrideComponent(LoginPage, { set: { template: '' } })
      .compileComponents();
  });

  it('alterna a visibilidade da senha e volta ao estado seguro', () => {
    const fixture = TestBed.createComponent(LoginPage);
    const component = fixture.componentInstance;

    expect(component.passwordVisible).toBeFalse();

    component.togglePasswordVisibility();
    expect(component.passwordVisible).toBeTrue();

    component.togglePasswordVisibility();
    expect(component.passwordVisible).toBeFalse();
  });
});
