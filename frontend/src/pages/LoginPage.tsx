import { useState, type FormEvent } from "react";
import { Link, Navigate } from "react-router-dom";
import { ApiError } from "../api";
import { useAuth } from "../auth";
import { ErrorBanner } from "../components/ui";

export function LoginPage() {
  const { isAuthenticated, login, notice } = useAuth();
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  if (isAuthenticated) return <Navigate to="/" replace />;

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await login(username, password);
    } catch (e) {
      // 403 carries the reason (pending approval, expired, suspended) in the message.
      setError(e instanceof ApiError ? e.message : "Erro inesperado.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <main className="login">
      <form className="login-card" onSubmit={submit}>
        <span className="brand-mark brand-mark-large" aria-hidden="true">
          R$
        </span>
        <h1>Cobranças</h1>
        <p className="muted">Controle de serviços, pagamentos e saldos em aberto.</p>
        <ErrorBanner message={error ?? notice} />
        <label htmlFor="username">E-mail</label>
        <input
          id="username"
          type="text"
          inputMode="email"
          autoComplete="username"
          value={username}
          onChange={(e) => setUsername(e.target.value)}
          autoCapitalize="none"
          required
        />
        <label htmlFor="password">Senha</label>
        <input
          id="password"
          type="password"
          autoComplete="current-password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          required
        />
        <button type="submit" className="button button-primary" disabled={busy}>
          {busy ? "Entrando…" : "Entrar"}
        </button>
        <p className="login-footer">
          Ainda não tem acesso? <Link to="/solicitar-acesso">Solicitar acesso</Link>
        </p>
      </form>
    </main>
  );
}
