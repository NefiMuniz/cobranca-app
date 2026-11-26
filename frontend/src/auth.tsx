import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { api, sessionStore, setAccessBlockedHandler, setToken, setUnauthorizedHandler, type Session } from "./api";
import type { Role } from "./types";

interface AuthContextValue {
  isAuthenticated: boolean;
  role: Role | null;
  name: string | null;
  /** Message shown on the login screen after the session ends for a reason (expired access, suspension). */
  notice: string | null;
  login: (username: string, password: string) => Promise<void>;
  logout: (notice?: string) => void;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<Session | null>(() => sessionStore.read());
  const [notice, setNotice] = useState<string | null>(null);

  const logout = useCallback((message?: string) => {
    sessionStore.write(null);
    setToken(null);
    setSession(null);
    setNotice(message ?? null);
  }, []);

  useEffect(() => {
    setUnauthorizedHandler(() => logout());
    setAccessBlockedHandler((message) => logout(message));
  }, [logout]);

  const login = useCallback(async (username: string, password: string) => {
    const response = await api.login(username, password);
    const next: Session = { token: response.token, expiresAt: response.expiresAt, role: response.role, name: response.name };
    sessionStore.write(next);
    setToken(next.token);
    setSession(next);
    setNotice(null);
  }, []);

  const value = useMemo<AuthContextValue>(
    () => ({
      isAuthenticated: session !== null,
      role: session?.role ?? null,
      name: session?.name ?? null,
      notice,
      login,
      logout,
    }),
    [session, notice, login, logout],
  );
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);
  if (!context) throw new Error("useAuth must be used inside <AuthProvider>");
  return context;
}
