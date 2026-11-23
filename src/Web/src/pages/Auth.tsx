import { useState, type FormEvent } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { api } from '../api';
import { useAuth } from '../auth';
import { Alert } from '../components/Alert';

export function AuthPage({ mode }: { mode: 'login' | 'register' }) {
  const { signIn } = useAuth();
  const navigate = useNavigate();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [fullName, setFullName] = useState('');
  const [error, setError] = useState('');

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setError('');
    try {
      signIn(mode === 'login' ? await api.login(email, password) : await api.register(email, password, fullName));
      navigate('/');
    } catch (err) {
      const e2 = err as Error & { status?: number };
      setError(e2.status === 401 ? 'Invalid email or password.' : e2.message);
    }
  };

  return (
    <form onSubmit={submit} className="mx-auto mt-10 max-w-sm space-y-3 rounded-lg bg-white p-6 shadow-sm">
      <h1 className="text-2xl font-bold">{mode === 'login' ? 'Sign in' : 'Create account'}</h1>
      {error && <Alert>{error}</Alert>}
      {mode === 'register' && (
        <label className="block text-sm">Full name<input required value={fullName} onChange={(e) => setFullName(e.target.value)} className="mt-1 block w-full rounded border px-3 py-2" /></label>
      )}
      <label className="block text-sm">Email<input required type="email" value={email} onChange={(e) => setEmail(e.target.value)} className="mt-1 block w-full rounded border px-3 py-2" /></label>
      <label className="block text-sm">Password<input required type="password" value={password} onChange={(e) => setPassword(e.target.value)} className="mt-1 block w-full rounded border px-3 py-2" /></label>
      <button className="w-full rounded-md bg-indigo-600 py-2 text-white">{mode === 'login' ? 'Sign in' : 'Create account'}</button>
      <p className="text-center text-sm">
        {mode === 'login' ? <Link to="/register" className="text-indigo-600">Create an account</Link> : <Link to="/login" className="text-indigo-600">I already have an account</Link>}
      </p>
    </form>
  );
}
