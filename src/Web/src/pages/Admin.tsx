import { useEffect, useState, type FormEvent } from 'react';
import { api, money, type Order, type Product } from '../api';
import { Alert } from '../components/Alert';
import { StatusBadge } from './Orders';

type Draft = Omit<Product, 'id'> & { id?: string };
const empty: Draft = { name: '', description: '', category: '', price: 0, imageUrl: null, availableStock: 0 };

export function Admin() {
  const [products, setProducts] = useState<Product[]>([]);
  const [orders, setOrders] = useState<Order[]>([]);
  const [draft, setDraft] = useState<Draft>(empty);
  const [error, setError] = useState('');

  const reload = () => {
    api.products().then((p) => setProducts(p.items)).catch((e) => setError(e.message));
    api.allOrders().then(setOrders).catch((e) => setError(e.message));
  };
  useEffect(reload, []);

  const save = async (e: FormEvent) => {
    e.preventDefault();
    setError('');
    try { await api.saveProduct(draft); setDraft(empty); reload(); } catch (err) { setError((err as Error).message); }
  };
  const remove = async (id: string) => {
    try { await api.deleteProduct(id); reload(); } catch (err) { setError((err as Error).message); }
  };

  const num = (k: 'price' | 'availableStock') => (e: React.ChangeEvent<HTMLInputElement>) => setDraft({ ...draft, [k]: Number(e.target.value) });

  return (
    <div className="space-y-8">
      {error && <Alert>{error}</Alert>}
      <section>
        <h1 className="mb-3 text-2xl font-bold">Products</h1>
        <form onSubmit={save} className="mb-4 grid gap-2 rounded-lg bg-white p-4 shadow-sm sm:grid-cols-6">
          <input aria-label="Name" placeholder="Name" value={draft.name} onChange={(e) => setDraft({ ...draft, name: e.target.value })} className="rounded border px-2 py-1 sm:col-span-2" />
          <input aria-label="Category" placeholder="Category" value={draft.category} onChange={(e) => setDraft({ ...draft, category: e.target.value })} className="rounded border px-2 py-1" />
          <input aria-label="Price" type="number" step="0.01" value={draft.price} onChange={num('price')} className="rounded border px-2 py-1" />
          <input aria-label="Stock" type="number" value={draft.availableStock} onChange={num('availableStock')} className="rounded border px-2 py-1" />
          <button className="rounded bg-indigo-600 px-3 py-1 text-white">{draft.id ? 'Update' : 'Add'}</button>
          <input aria-label="Description" placeholder="Description" value={draft.description} onChange={(e) => setDraft({ ...draft, description: e.target.value })} className="rounded border px-2 py-1 sm:col-span-6" />
        </form>
        <table className="w-full rounded-lg bg-white text-sm shadow-sm">
          <thead><tr className="text-left"><th className="p-2">Name</th><th>Category</th><th>Price</th><th>Stock</th><th /></tr></thead>
          <tbody>
            {products.map((p) => (
              <tr key={p.id} className="border-t">
                <td className="p-2">{p.name}</td><td>{p.category}</td><td>{money(p.price)}</td><td>{p.availableStock}</td>
                <td className="space-x-2 text-right pr-2">
                  <button onClick={() => setDraft(p)} className="text-indigo-600">Edit</button>
                  <button onClick={() => remove(p.id)} className="text-red-600">Delete</button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>
      <section>
        <h2 className="mb-3 text-xl font-bold">All orders</h2>
        <ul className="divide-y rounded-lg bg-white text-sm shadow-sm">
          {orders.map((o) => (
            <li key={o.id} className="flex justify-between p-2"><span>#{o.id.slice(0, 8)}</span><span>{money(o.total)}</span><StatusBadge status={o.status} /></li>
          ))}
        </ul>
      </section>
    </div>
  );
}
