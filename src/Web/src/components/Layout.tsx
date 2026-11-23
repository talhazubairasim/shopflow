import { Link, NavLink, Outlet, useNavigate } from 'react-router-dom';
import { useAuth } from '../auth';

const link = ({ isActive }: { isActive: boolean }) =>
  `px-3 py-2 rounded-md text-sm font-medium ${isActive ? 'bg-indigo-100 text-indigo-800' : 'text-slate-700 hover:bg-slate-100'}`;

export function Layout() {
  const { user, isAdmin, signOut } = useAuth();
  const navigate = useNavigate();
  return (
    <div className="min-h-screen">
      <header className="bg-white shadow-sm">
        <div className="mx-auto flex max-w-6xl items-center justify-between px-4 py-3">
          <Link to="/" className="text-xl font-bold text-indigo-600">ShopFlow</Link>
          <nav className="flex items-center gap-1">
            <NavLink to="/" end className={link}>Shop</NavLink>
            {user && <NavLink to="/basket" className={link}>Basket</NavLink>}
            {user && <NavLink to="/orders" className={link}>Orders</NavLink>}
            {isAdmin && <NavLink to="/admin" className={link}>Admin</NavLink>}
            {user ? (
              <button className="ml-2 rounded-md border px-3 py-1.5 text-sm" onClick={() => { signOut(); navigate('/'); }}>
                Sign out {user.fullName.split(' ')[0]}
              </button>
            ) : (
              <NavLink to="/login" className={link}>Sign in</NavLink>
            )}
          </nav>
        </div>
      </header>
      <main className="mx-auto max-w-6xl p-4">
        <Outlet />
      </main>
    </div>
  );
}
