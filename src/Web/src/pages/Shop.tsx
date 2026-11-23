import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api, money, type Product } from '../api';
import { useAuth } from '../auth';
import { Alert } from '../components/Alert';

export function Shop() {
  const { user } = useAuth();
  const navigate = useNavigate();
  const [products, setProducts] = useState<Product[]>([]);
  const [categories, setCategories] = useState<string[]>([]);
  const [search, setSearch] = useState('');
  const [category, setCategory] = useState('');
  const [message, setMessage] = useState<{ ok: boolean; text: string } | null>(null);

  useEffect(() => { api.categories().then(setCategories).catch(() => {}); }, []);
  useEffect(() => {
    const t = setTimeout(() => {
      api.products(search, category).then((p) => setProducts(p.items)).catch((e) => setMessage({ ok: false, text: e.message }));
    }, 250);
    return () => clearTimeout(t);
  }, [search, category]);

  const add = async (p: Product) => {
    if (!user) return navigate('/login');
    try {
      await api.addToBasket(p.id);
      setMessage({ ok: true, text: `${p.name} added to your basket` });
    } catch (e) {
      setMessage({ ok: false, text: (e as Error).message });
    }
  };

  return (
    <div>
      <div className="mb-4 flex flex-wrap gap-2">
        <input aria-label="Search products" placeholder="Search products" value={search} onChange={(e) => setSearch(e.target.value)}
          className="w-64 rounded-md border px-3 py-2" />
        <select aria-label="Category" value={category} onChange={(e) => setCategory(e.target.value)} className="rounded-md border px-3 py-2">
          <option value="">All categories</option>
          {categories.map((c) => <option key={c}>{c}</option>)}
        </select>
      </div>
      {message && <Alert kind={message.ok ? 'success' : 'error'}>{message.text}</Alert>}
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        {products.map((p) => (
          <article key={p.id} aria-label={p.name} className="flex flex-col rounded-lg bg-white p-4 shadow-sm">
            <span className="text-xs uppercase text-slate-500">{p.category}</span>
            <h2 className="font-semibold">{p.name}</h2>
            <p className="flex-1 text-sm text-slate-600">{p.description}</p>
            <div className="mt-3 flex items-center justify-between">
              <span className="font-bold">{money(p.price)}</span>
              {p.availableStock > 0 ? (
                <button onClick={() => add(p)} className="rounded-md bg-indigo-600 px-3 py-1.5 text-sm text-white hover:bg-indigo-700">Add to basket</button>
              ) : (
                <span className="text-sm text-red-600">Out of stock</span>
              )}
            </div>
          </article>
        ))}
        {products.length === 0 && <p className="text-slate-500">No products match.</p>}
      </div>
    </div>
  );
}
