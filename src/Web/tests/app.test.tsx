import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ApiError, api, request, session } from '../src/api';
import { admin, buyer, mockApi, product, renderAt } from './helpers';

const paged = (items: unknown[]) => ({ items, page: 1, pageSize: 50, total: items.length });

describe('shop', () => {
  it('lists products, filters, and sends anonymous buyers to sign in', async () => {
    const user = userEvent.setup();
    const calls = mockApi({
      'GET /api/catalog/categories': ['Books', 'Electronics'],
      'GET /api/catalog/products': (_i: unknown, url: URL) =>
        ({ body: paged(url.searchParams.get('category') === 'Books' ? [] : [product(), product({ id: 'p2', name: 'Hub', availableStock: 0 })]) }),
    });
    renderAt('/');
    expect(await screen.findByRole('article', { name: 'Keyboard' })).toBeInTheDocument();
    expect(within(screen.getByRole('article', { name: 'Hub' })).getByText('Out of stock')).toBeInTheDocument();
    await user.selectOptions(screen.getByLabelText('Category'), 'Books');
    expect(await screen.findByText('No products match.')).toBeInTheDocument();
    expect(calls.some((c) => c.key === 'GET /api/catalog/products')).toBe(true);
  });

  it('redirects to sign in when adding without an account', async () => {
    const user = userEvent.setup();
    mockApi({ 'GET /api/catalog/categories': [], 'GET /api/catalog/products': paged([product()]) });
    renderAt('/');
    await user.click(await screen.findByText('Add to basket'));
    expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeInTheDocument();
  });

  it('adds to the basket and reports stock errors', async () => {
    const user = userEvent.setup();
    let fail = false;
    mockApi({
      'GET /api/catalog/categories': [], 'GET /api/catalog/products': paged([product()]),
      'POST /api/basket/items': () => (fail ? { status: 409, body: { error: 'Only 5 of Keyboard in stock' } } : { body: { items: [] } }),
    });
    renderAt('/', buyer);
    await user.click(await screen.findByText('Add to basket'));
    expect(await screen.findByRole('status')).toHaveTextContent('Keyboard added');
    fail = true;
    await user.click(screen.getByText('Add to basket'));
    expect(await screen.findByRole('alert')).toHaveTextContent('Only 5 of Keyboard');
  });
});

describe('basket and checkout', () => {
  const basket = { buyerId: 'u1', total: 160, items: [{ productId: 'p1', productName: 'Keyboard', unitPrice: 80, quantity: 2 }] };

  it('shows, updates and empties the basket', async () => {
    const user = userEvent.setup();
    mockApi({
      'GET /api/basket': basket,
      'PUT /api/basket/items/p1': { ...basket, total: 240, items: [{ ...basket.items[0], quantity: 3 }] },
      'DELETE /api/basket/items/p1': { buyerId: 'u1', total: 0, items: [] },
    });
    renderAt('/basket', buyer);
    expect(await screen.findByText('Total $160.00')).toBeInTheDocument();
    await user.type(screen.getByLabelText('Quantity of Keyboard'), '{backspace}3');
    expect(await screen.findByText('Total $240.00')).toBeInTheDocument();
    await user.click(screen.getByLabelText('Remove Keyboard'));
    expect(await screen.findByText(/Your basket is empty/)).toBeInTheDocument();
  });

  it('places an order and follows it to confirmation', async () => {
    const user = userEvent.setup();
    let polls = 0;
    const order = (status: string) => ({ id: 'o1abcdefgh', status, total: 160, lines: basket.items, cardLast4: '4242', transactionId: status === 'Confirmed' ? 'txn_1' : null, failureReason: null, createdAt: new Date().toISOString() });
    const calls = mockApi({
      'POST /api/basket/checkout': { orderId: 'o1abcdefgh', total: 160 },
      'GET /api/orders/o1abcdefgh': () => { polls++; return polls === 1 ? { status: 404, body: {} } : { body: order(polls < 3 ? 'AwaitingPayment' : 'Confirmed') }; },
    });
    renderAt('/checkout', buyer);
    for (const [label, value] of [['Street', '1 Main'], ['City', 'Isb'], ['Country', 'PK'], ['Postal code', '44000'], ['Card number', '4242 4242 4242 4242']])
      await user.type(screen.getByLabelText(label), value);
    await user.click(screen.getByRole('button', { name: 'Place order' }));
    expect(await screen.findByText('Transaction txn_1', {}, { timeout: 5000 })).toBeInTheDocument();
    expect(calls.find((c) => c.key === 'POST /api/basket/checkout')!.body).toMatchObject({ cardNumber: '4242424242424242', fullName: 'Buyer One' });
  });

  it('shows checkout errors and cancelled orders', async () => {
    const user = userEvent.setup();
    mockApi({
      'POST /api/basket/checkout': { status: 400, body: { errors: { CardNumber: ['Card number must be 12–19 digits'] } } },
      'GET /api/orders/o2': { id: 'o2', status: 'Cancelled', total: 10, lines: [], cardLast4: '0002', failureReason: 'Card declined by issuer', createdAt: new Date().toISOString() },
    });
    renderAt('/checkout', buyer);
    for (const label of ['Street', 'City', 'Country', 'Postal code', 'Card number']) await user.type(screen.getByLabelText(label), 'x');
    await user.click(screen.getByRole('button', { name: 'Place order' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('12–19 digits');

    renderAt('/orders/o2', buyer);
    expect(await screen.findByText('Card declined by issuer')).toBeInTheDocument();
  });
});

describe('orders, auth and admin', () => {
  it('lists orders and notifications', async () => {
    mockApi({
      'GET /api/orders': [{ id: 'abcdef123', status: 'Confirmed', total: 10, lines: [], cardLast4: '4242', createdAt: new Date().toISOString() }],
      'GET /api/notifications': [{ id: 'n1', subject: 'Order abcdef12 confirmed', body: 'Thanks', sentAt: '' }],
    });
    renderAt('/orders', buyer);
    expect(await screen.findByText('#abcdef12')).toBeInTheDocument();
    expect(await screen.findByText('Order abcdef12 confirmed')).toBeInTheDocument();
  });

  it('signs in, registers and signs out', async () => {
    const user = userEvent.setup();
    mockApi({
      'POST /api/identity/login': (init?: RequestInit) => JSON.parse(String(init!.body)).password === 'right' ? { body: buyer } : { status: 401 },
      'POST /api/identity/register': { status: 400, body: { errors: { DuplicateEmail: ['Email is already taken.'] } } },
      'GET /api/catalog/categories': [], 'GET /api/catalog/products': paged([]),
    });
    renderAt('/login');
    await user.type(screen.getByLabelText('Email'), 'b@x.dev');
    await user.type(screen.getByLabelText('Password'), 'wrong');
    await user.click(screen.getByRole('button', { name: 'Sign in' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Invalid email or password');
    await user.clear(screen.getByLabelText('Password'));
    await user.type(screen.getByLabelText('Password'), 'right');
    await user.click(screen.getByRole('button', { name: 'Sign in' }));
    expect(await screen.findByText('Sign out Buyer')).toBeInTheDocument();
    expect(session.get()?.userId).toBe('u1');
    await user.click(screen.getByText('Sign out Buyer'));
    expect(session.get()).toBeNull();

    await user.click(screen.getByRole('link', { name: 'Sign in' }));
    await user.click(screen.getByText('Create an account'));
    await user.type(screen.getByLabelText('Full name'), 'Dup');
    await user.type(screen.getByLabelText('Email'), 'b@x.dev');
    await user.type(screen.getByLabelText('Password'), 'Str0ng!Pass');
    await user.click(screen.getByRole('button', { name: 'Create account' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('already taken');
  });

  it('protects pages by role', async () => {
    mockApi({ 'GET /api/catalog/categories': [], 'GET /api/catalog/products': paged([]) });
    renderAt('/admin', buyer);
    expect(await screen.findByText('No products match.')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Admin' })).not.toBeInTheDocument();
  });

  it('lets admins add, edit and delete products', async () => {
    const user = userEvent.setup();
    const calls = mockApi({
      'GET /api/catalog/products': paged([product()]),
      'GET /api/orders/all': [{ id: 'zzz111222', status: 'Submitted', total: 5, lines: [], cardLast4: '1', createdAt: '' }],
      'POST /api/catalog/products': product({ id: 'p9' }),
      'PUT /api/catalog/products/p1': product(),
      'DELETE /api/catalog/products/p1': { status: 204 },
    });
    renderAt('/admin', admin);
    expect(await screen.findByText('#zzz11122')).toBeInTheDocument();
    await user.type(screen.getByLabelText('Name'), 'Webcam');
    await user.type(screen.getByLabelText('Category'), 'Electronics');
    await user.click(screen.getByRole('button', { name: 'Add' }));
    await waitFor(() => expect(calls.some((c) => c.key === 'POST /api/catalog/products')).toBe(true));
    await user.click(await screen.findByText('Edit'));
    await user.click(screen.getByRole('button', { name: 'Update' }));
    await waitFor(() => expect(calls.some((c) => c.key === 'PUT /api/catalog/products/p1')).toBe(true));
    await user.click(screen.getByText('Delete'));
    await waitFor(() => expect(calls.some((c) => c.key === 'DELETE /api/catalog/products/p1')).toBe(true));
  });
});

describe('api client', () => {
  it('maps errors and handles empty bodies', async () => {
    mockApi({
      'GET /a': { status: 401 }, 'GET /b': { status: 500, body: { title: 'Boom' } }, 'GET /c': { status: 204 }, 'GET /d': { status: 200 },
    });
    await expect(request('/a')).rejects.toMatchObject({ status: 401, message: 'Please sign in.' });
    await expect(request('/b')).rejects.toBeInstanceOf(ApiError);
    await expect(request('/c')).resolves.toBeUndefined();
    await expect(request('/d')).resolves.toBeUndefined();
    localStorage.setItem('shopflow.session', '{bad');
    expect(session.get()).toBeNull();
    expect(typeof api.products).toBe('function');
  });
});
