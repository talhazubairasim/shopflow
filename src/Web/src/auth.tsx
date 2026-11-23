import { createContext, useContext, useState, type ReactNode } from 'react';
import { session, type Session } from './api';

interface AuthState {
  user: Session | null;
  isAdmin: boolean;
  signIn: (s: Session) => void;
  signOut: () => void;
}

const AuthContext = createContext<AuthState | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<Session | null>(() => session.get());
  const value: AuthState = {
    user,
    isAdmin: !!user?.roles.includes('admin'),
    signIn: (s) => { session.set(s); setUser(s); },
    signOut: () => { session.set(null); setUser(null); },
  };
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

// eslint-disable-next-line react-refresh/only-export-components
export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used inside AuthProvider');
  return ctx;
}
