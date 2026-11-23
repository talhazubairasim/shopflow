import { useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { api, type CheckoutForm } from '../api';
import { useAuth } from '../auth';
import { Alert } from '../components/Alert';

const fields: [keyof CheckoutForm, string][] = [
  ['fullName', 'Full name'], ['street', 'Street'], ['city', 'City'], ['country', 'Country'], ['postalCode', 'Postal code'], ['cardNumber', 'Card number'],
];

export function Checkout() {
  const { user } = useAuth();
  const navigate = useNavigate();
  const [form, setForm] = useState<CheckoutForm>({ fullName: user?.fullName ?? '', street: '', city: '', country: '', postalCode: '', cardNumber: '' });
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setError('');
    setBusy(true);
    try {
      const { orderId } = await api.checkout({ ...form, cardNumber: form.cardNumber.replace(/\s/g, '') });
      navigate(`/orders/${orderId}`);
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <form onSubmit={submit} className="max-w-md space-y-3 rounded-lg bg-white p-6 shadow-sm">
      <h1 className="text-2xl font-bold">Checkout</h1>
      {error && <Alert>{error}</Alert>}
      {fields.map(([key, label]) => (
        <label key={key} className="block text-sm">
          {label}
          <input required value={form[key]} onChange={(e) => setForm({ ...form, [key]: e.target.value })} className="mt-1 block w-full rounded border px-3 py-2" />
        </label>
      ))}
      <p className="text-xs text-slate-500">Test cards: 4242 4242 4242 4242 succeeds, 4000 0000 0000 0002 is declined.</p>
      <button disabled={busy} className="w-full rounded-md bg-indigo-600 py-2 text-white disabled:opacity-50">{busy ? 'Placing order…' : 'Place order'}</button>
    </form>
  );
}
