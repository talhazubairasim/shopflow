import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api, money, type Basket as BasketModel } from '../api';
import { Alert } from '../components/Alert';

export function Basket() {
  const [basket, setBasket] = useState<BasketModel | null>(null);
  const [error, setError] = useState('');

  useEffect(() => { api.basket().then(setBasket).catch((e) => setError(e.message)); }, []);

  const run = (p: Promise<BasketModel>) => p.then(setBasket).catch((e) => setError(e.message));

  if (!basket) return error ? <Alert>{error}</Alert> : <p>Loading…</p>;
  if (basket.items.length === 0) return <p>Your basket is empty. <Link className="text-indigo-600" to="/">Keep shopping</Link></p>;

  return (
    <div className="max-w-2xl">
      <h1 className="mb-4 text-2xl font-bold">Basket</h1>
      {error && <Alert>{error}</Alert>}
      <ul className="divide-y rounded-lg bg-white shadow-sm">
        {basket.items.map((i) => (
          <li key={i.productId} className="flex items-center justify-between p-3">
            <span>{i.productName}</span>
            <span className="flex items-center gap-2">
              <input aria-label={`Quantity of ${i.productName}`} type="number" min={1} max={99} value={i.quantity}
                onChange={(e) => run(api.setQuantity(i.productId, Number(e.target.value)))} className="w-16 rounded border px-2 py-1" />
              <span className="w-20 text-right">{money(i.unitPrice * i.quantity)}</span>
              <button aria-label={`Remove ${i.productName}`} onClick={() => run(api.removeFromBasket(i.productId))} className="text-red-600">✕</button>
            </span>
          </li>
        ))}
      </ul>
      <div className="mt-4 flex items-center justify-between">
        <span className="text-lg font-bold">Total {money(basket.total)}</span>
        <Link to="/checkout" className="rounded-md bg-indigo-600 px-4 py-2 text-white">Checkout</Link>
      </div>
    </div>
  );
}
