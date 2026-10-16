import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { AuthService, InvalidCredentialsError } from './auth.service';

describe('AuthService', () => {
  let auth: AuthService;
  beforeEach(() => (auth = TestBed.inject(AuthService)));

  it('corresponsal entra con rol agent', async () => {
    const role = await firstValueFrom(auth.login({ email: ' Rosa@demo.relia ', password: 'demo1234' }));
    expect(role).toBe('agent');
    expect(auth.authenticated()).toBe(true);
  });

  it('admin entra con rol admin', async () => {
    expect(await firstValueFrom(auth.login({ email: 'admin@relia.co', password: 'admin1234' }))).toBe(
      'admin',
    );
  });

  it('rechaza credenciales incorrectas', async () => {
    await expect(
      firstValueFrom(auth.login({ email: 'rosa@demo.relia', password: 'admin1234' })),
    ).rejects.toBeInstanceOf(InvalidCredentialsError);
    expect(auth.authenticated()).toBe(false);
  });
});
