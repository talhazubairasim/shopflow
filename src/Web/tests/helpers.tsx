import { render } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { AuthProvider } from '../src/auth';
import { AppRoutes } from '../src/App';
import { session, type Session } from '../src/api';

type Handler = (init: RequestInit | undefined, url: URL) => { status?: number; body?: unknown };

/** A plain `{ status, body? }` object is treated as a response spec rather than a JSON body. */
const isSpec = (v: unknown): v is { status: number; body?: unknown } =>
  !!v && typeof v === 'object' && !Array.isArray(v) && 'status' in v && typeof (v as { status: unknown }).status === 'number'
  && Object.keys(v).every((k) => k === 'status' || k === 'body');

/** Routes fetch calls to handlers keyed by "METHOD /path" (query string ignored). */
export function mockApi(routes: Record<string, Handler | unknown>) {
  const calls: { key: string; body: unknown }[] = [];
  vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
    const url = new URL(String(input), 'http://localhost');
    const key = `${init?.method ?? 'GET'} ${url.pathname}`;
    calls.push({ key, body: init?.body ? JSON.parse(String(init.body)) : undefined });
    const route = routes[key];
    if (route === undefined) return new Response(JSON.stringify({ error: `no mock for ${key}` }), { status: 500 });
    const { status = 200, body } = typeof route === 'function' ? (route as Handler)(init, url) : isSpec(route) ? route : { body: route };
    return new Response(body === undefined ? null : JSON.stringify(body), { status });
  });
  return calls;
}

export const buyer: Session = { token: 't', userId: 'u1', email: 'b@x.dev', fullName: 'Buyer One', roles: ['customer'] };
export const admin: Session = { ...buyer, userId: 'a1', fullName: 'Admin', roles: ['admin', 'customer'] };

export function renderAt(path: string, user: Session | null = null) {
  if (user) session.set(user);
  return render(
    <AuthProvider>
      <MemoryRouter initialEntries={[path]} future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
        <AppRoutes />
      </MemoryRouter>
    </AuthProvider>,
  );
}

export const product = (over: Record<string, unknown> = {}) => ({
  id: 'p1', name: 'Keyboard', description: 'Clicky', category: 'Electronics', price: 80, imageUrl: null, availableStock: 5, ...over,
});
