import { useState, type FormEvent } from "react";
import { Link } from "react-router-dom";
import { api, ApiError } from "../api";
import { ErrorBanner, Field } from "../components/ui";

export function SignupPage() {
  const [name, setName] = useState("");
  const [email, setEmail] = useState("");
  const [phone, setPhone] = useState("");
  const [password, setPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [website, setWebsite] = useState(""); // honeypot
  const [errors, setErrors] = useState<Record<string, string[]>>({});
  const [message, setMessage] = useState<string | null>(null);
  const [done, setDone] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (password !== confirm) {
      setErrors({ confirm: ["As senhas não conferem."] });
      return;
    }

    setBusy(true);
    setMessage(null);
    setErrors({});
    try {
      const response = await api.signup({ name, email, phone, password, website });
      setDone(response.message);
    } catch (e) {
      if (e instanceof ApiError) {
        setErrors(e.fieldErrors);
        setMessage(e.message);
      } else {
        setMessage("Erro inesperado.");
      }
    } finally {
      setBusy(false);
    }
  };

  if (done) {
    return (
      <main className="login">
        <div className="login-card" role="status">
          <span className="brand-mark brand-mark-large" aria-hidden="true">
            ✓
          </span>
          <h1>Solicitação enviada</h1>
          <p className="muted">{done}</p>
          <Link to="/login" className="button button-ghost">
            Voltar para o login
          </Link>
        </div>
      </main>
    );
  }

  return (
    <main className="login">
      <form className="login-card form" onSubmit={submit} noValidate>
        <h1>Solicitar acesso</h1>
        <p className="muted">
          Preencha seus dados. Assim que o acesso for liberado, você entra com o e-mail e a senha escolhidos aqui.
        </p>
        <ErrorBanner message={message} />

        <Field label="Nome ou nome do negócio" error={errors.name}>
          {(id, describedBy) => (
            <input id={id} aria-describedby={describedBy} value={name} onChange={(e) => setName(e.target.value)} autoComplete="organization" />
          )}
        </Field>
        <Field label="E-mail" error={errors.email}>
          {(id, describedBy) => (
            <input
              id={id}
              type="email"
              aria-describedby={describedBy}
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              autoComplete="email"
              autoCapitalize="none"
            />
          )}
        </Field>
        <Field label="WhatsApp com DDD" error={errors.phone}>
          {(id, describedBy) => (
            <input
              id={id}
              type="tel"
              inputMode="tel"
              aria-describedby={describedBy}
              value={phone}
              onChange={(e) => setPhone(e.target.value)}
              autoComplete="tel"
              placeholder="(77) 99999-0000"
            />
          )}
        </Field>
        <Field label="Senha" error={errors.password} hint="Mínimo de 8 caracteres.">
          {(id, describedBy) => (
            <input
              id={id}
              type="password"
              aria-describedby={describedBy}
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              autoComplete="new-password"
            />
          )}
        </Field>
        <Field label="Confirme a senha" error={errors.confirm}>
          {(id, describedBy) => (
            <input
              id={id}
              type="password"
              aria-describedby={describedBy}
              value={confirm}
              onChange={(e) => setConfirm(e.target.value)}
              autoComplete="new-password"
            />
          )}
        </Field>

        {/* Honeypot: invisible to people and screen readers; bots that fill every field are ignored by the API. */}
        <div className="honeypot" aria-hidden="true">
          <label htmlFor="website">Site</label>
          <input id="website" tabIndex={-1} autoComplete="off" value={website} onChange={(e) => setWebsite(e.target.value)} />
        </div>

        <button type="submit" className="button button-primary" disabled={busy}>
          {busy ? "Enviando…" : "Enviar solicitação"}
        </button>
        <p className="login-footer">
          Já tem acesso? <Link to="/login">Entrar</Link>
        </p>
      </form>
    </main>
  );
}
