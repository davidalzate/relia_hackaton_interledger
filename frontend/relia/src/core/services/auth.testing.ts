import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';

import { Role } from '../../shared/schemas/auth.schema';
import { AuthService } from './auth.service';

// Solo specs: requiere provideHttpClient() y provideHttpClientTesting() en el TestBed
const ME: Record<Role, object> = {
  agent: {
    role: 'agent',
    email: 'rosa@demo.relia',
    name: 'Rosa Gómez',
    agent: { id: 'agent-rosa', name: 'Tienda Doña Rosa' },
  },
  admin: { role: 'admin', email: 'admin@relia.co', name: 'Admin Relia', agent: null },
};

export async function loginAs(role: Role): Promise<Role> {
  const ctrl = TestBed.inject(HttpTestingController);
  const done = firstValueFrom(
    TestBed.inject(AuthService).login({ email: 'x@relia.co', password: 'x' }),
  );
  ctrl.expectOne('/api/auth/login').flush({
    accessToken: `tok-${role}`,
    expiresIn: 3600,
    role,
    agentId: role === 'agent' ? 'agent-rosa' : null,
  });
  ctrl.expectOne('/api/me').flush(ME[role]);
  return done;
}
