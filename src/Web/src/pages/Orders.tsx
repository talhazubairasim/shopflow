import { useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { api, money, type Notice, type Order } from '../api';
import { Alert } from '../components/Alert';

const badge: Record<Order['status'], string> = {
  Submitted: 'bg-yellow-100 text-yellow-800',
  AwaitingPayment: 'bg-blue-100 text-blue-800',
  Confirmed: 'bg-green-100 text-green-800',
  Cancelled: 'bg-red-100 text-red-800',
};
const label: Record<Order['status'], string> = {
  Submitted: 'Reserving stock', AwaitingPayment: 'Processing payment', Confirmed: 'Confirmed', Cancelled: 'Cancelled',
};

export const StatusBadge = ({ status }: { status: Order['status'] }) =>
  <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${badge[status]}`}>{label[status]}</span>;

export function Orders() {
  const [orders, setOrders] = useState<Order[]>([]);
  const [notices, setNotices] = useState<Notice[]>([]);
  const [error, setError] = useState('');
  useEffect(() => {
    api.orders().then(setOrders).catch((e) => setError(e.message));
    api.notifications().then(setNotices).catch(() => {});
  }, []);
  return (
    <div className="grid gap-6 md:grid-cols-3">
      <section className="md:col-span-2">
        <h1 className="mb-4 text-2xl font-bold">Your orders</h1>
        {error && <Alert>{error}</Alert>}
        <ul className="divide-y rounded-lg bg-white shadow-sm">
          {orders.map((o) => (
            <li key={o.id} className="flex items-center justify-between p-3">
              <Link to={`/orders/${o.id}`} className="text-indigo-600">#{o.id.slice(0, 8)}</Link>
              <span>{new Date(o.createdAt).toLocaleString()}</span>
              <span>{money(o.total)}</span>
              <StatusBadge status={o.status} />
            </li>
          ))}
          {orders.length === 0 && <li className="p-3 text-slate-500">No orders yet.</li>}
        </ul>
      </section>
      <aside>
        <h2 className="mb-2 font-semibold">Notifications</h2>
        <ul className="space-y-2">
          {notices.map((n) => (
            <li key={n.id} className="rounded-md bg-white p-3 text-sm shadow-sm"><strong>{n.subject}</strong><p>{n.body}</p></li>
          ))}
          {notices.length === 0 && <li className="text-sm text-slate-500">Nothing yet.</li>}
        </ul>
      </aside>
    </div>
  );
}

const STEPS: Order['status'][] = ['Submitted', 'AwaitingPayment', 'Confirmed'];

/** Polls the order while the saga is still working on it. */
export function OrderDetail({ pollMs = 1000 }: { pollMs?: number }) {
  const { id = '' } = useParams();
  const [order, setOrder] = useState<Order | null>(null);
  const [error, setError] = useState('');

  useEffect(() => {
    let stop = false;
    let timer: ReturnType<typeof setTimeout>;
    const load = () => api.order(id)
      .then((o) => { if (stop) return; setOrder(o); if (o.status !== 'Confirmed' && o.status !== 'Cancelled') timer = setTimeout(load, pollMs); })
      .catch((e) => { if (stop) return; if (e.status === 404) timer = setTimeout(load, pollMs); else setError(e.message); });
    load();
    return () => { stop = true; clearTimeout(timer); };
  }, [id, pollMs]);

  if (error) return <Alert>{error}</Alert>;
  if (!order) return <p>Placing your order…</p>;
  const reached = order.status === 'Cancelled' ? -1 : STEPS.indexOf(order.status);
  return (
    <div className="max-w-2xl rounded-lg bg-white p-6 shadow-sm">
      <div className="mb-4 flex items-center justify-between">
        <h1 className="text-xl font-bold">Order #{order.id.slice(0, 8)}</h1>
        <StatusBadge status={order.status} />
      </div>
      <ol className="mb-4 flex gap-2 text-sm" aria-label="Progress">
        {['Stock reserved', 'Payment', 'Confirmed'].map((s, i) => (
          <li key={s} className={`flex-1 rounded p-2 text-center ${i <= reached ? 'bg-green-100' : 'bg-slate-100'}`}>{s}</li>
        ))}
      </ol>
      {order.status === 'Cancelled' && <Alert>{order.failureReason}</Alert>}
      <ul className="mb-3 text-sm">
        {order.lines.map((l) => <li key={l.productId}>{l.quantity} × {l.productName} — {money(l.unitPrice * l.quantity)}</li>)}
      </ul>
      <p className="font-bold">Total {money(order.total)} · card ending {order.cardLast4}</p>
      {order.transactionId && <p className="text-xs text-slate-500">Transaction {order.transactionId}</p>}
    </div>
  );
}
