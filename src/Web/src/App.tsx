import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom';
import type { ReactNode } from 'react';
import { AuthProvider, useAuth } from './auth';
import { Layout } from './components/Layout';
import { Shop } from './pages/Shop';
import { Basket } from './pages/Basket';
import { Checkout } from './pages/Checkout';
import { OrderDetail, Orders } from './pages/Orders';
import { AuthPage } from './pages/Auth';
import { Admin } from './pages/Admin';

function RequireUser({ admin = false, children }: { admin?: boolean; children: ReactNode }) {
  const { user, isAdmin } = useAuth();
  if (!user) return <Navigate to="/login" replace />;
  if (admin && !isAdmin) return <Navigate to="/" replace />;
  return <>{children}</>;
}

export function AppRoutes() {
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<Shop />} />
        <Route path="login" element={<AuthPage mode="login" />} />
        <Route path="register" element={<AuthPage mode="register" />} />
        <Route path="basket" element={<RequireUser><Basket /></RequireUser>} />
        <Route path="checkout" element={<RequireUser><Checkout /></RequireUser>} />
        <Route path="orders" element={<RequireUser><Orders /></RequireUser>} />
        <Route path="orders/:id" element={<RequireUser><OrderDetail /></RequireUser>} />
        <Route path="admin" element={<RequireUser admin><Admin /></RequireUser>} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Route>
    </Routes>
  );
}

export default function App() {
  return (
    <AuthProvider>
      <BrowserRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
        <AppRoutes />
      </BrowserRouter>
    </AuthProvider>
  );
}
