export interface Product { id: string; name: string; description: string; category: string; price: number; imageUrl?: string | null; availableStock: number }
export interface Paged<T> { items: T[]; page: number; pageSize: number; total: number }
export interface BasketItem { productId: string; productName: string; unitPrice: number; quantity: number }
export interface Basket { buyerId: string; items: BasketItem[]; total: number }
export interface OrderLine { productId: string; productName: string; unitPrice: number; quantity: number }
export interface Order {
  id: string; status: 'Submitted' | 'AwaitingPayment' | 'Confirmed' | 'Cancelled'; total: number; lines: OrderLine[];
  cardLast4: string; transactionId?: string | null; failureReason?: string | null; createdAt: string;
}
export interface Session { token: string; userId: string; email: string; fullName: string; roles: string[] }
export interface Notice { id: string; subject: string; body: string; sentAt: string }
export interface CheckoutForm { fullName: string; street: string; city: string; country: string; postalCode: string; cardNumber: string }

export class ApiError extends Error {
  constructor(public status: number, message: string) { super(message); }
}

const SESSION_KEY = 'shopflow.session';

export const session = {
  get(): Session | null {
    try { return JSON.parse(localStorage.getItem(SESSION_KEY) ?? 'null'); } catch { return null; }
  },
  set(s: Session | null) {
    if (s) localStorage.setItem(SESSION_KEY, JSON.stringify(s)); else localStorage.removeItem(SESSION_KEY);
  },
};

async function errorMessage(res: Response): Promise<string> {
  try {
    const body = await res.json();
    if (body.errors) return Object.values(body.errors as Record<string, string[]>).flat().join(' ');
    return body.error ?? body.title ?? res.statusText;
  } catch {
    return res.status === 401 ? 'Please sign in.' : res.statusText || 'Request failed';
  }
}

export async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const token = session.get()?.token;
  const res = await fetch(path, {
    ...init,
    headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}), ...init.headers },
  });
  if (!res.ok) throw new ApiError(res.status, await errorMessage(res));
  if (res.status === 204) return undefined as T;
  const text = await res.text();
  return (text ? JSON.parse(text) : undefined) as T;
}

const json = (body: unknown) => JSON.stringify(body);

export const api = {
  products: (search = '', category = '') =>
    request<Paged<Product>>(`/api/catalog/products?pageSize=50&search=${encodeURIComponent(search)}&category=${encodeURIComponent(category)}`),
  categories: () => request<string[]>('/api/catalog/categories'),
  saveProduct: (p: Omit<Product, 'id'> & { id?: string }) =>
    p.id ? request<Product>(`/api/catalog/products/${p.id}`, { method: 'PUT', body: json(p) })
         : request<Product>('/api/catalog/products', { method: 'POST', body: json(p) }),
  deleteProduct: (id: string) => request<void>(`/api/catalog/products/${id}`, { method: 'DELETE' }),
  basket: () => request<Basket>('/api/basket'),
  addToBasket: (productId: string, quantity = 1) => request<Basket>('/api/basket/items', { method: 'POST', body: json({ productId, quantity }) }),
  setQuantity: (productId: string, quantity: number) => request<Basket>(`/api/basket/items/${productId}`, { method: 'PUT', body: json({ productId, quantity }) }),
  removeFromBasket: (productId: string) => request<Basket>(`/api/basket/items/${productId}`, { method: 'DELETE' }),
  checkout: (form: CheckoutForm) => request<{ orderId: string; total: number }>('/api/basket/checkout', { method: 'POST', body: json(form) }),
  orders: () => request<Order[]>('/api/orders'),
  order: (id: string) => request<Order>(`/api/orders/${id}`),
  allOrders: () => request<Order[]>('/api/orders/all'),
  notifications: () => request<Notice[]>('/api/notifications'),
  login: (email: string, password: string) => request<Session>('/api/identity/login', { method: 'POST', body: json({ email, password }) }),
  register: (email: string, password: string, fullName: string) =>
    request<Session>('/api/identity/register', { method: 'POST', body: json({ email, password, fullName }) }),
};

export const money = (n: number) => `$${n.toFixed(2)}`;
