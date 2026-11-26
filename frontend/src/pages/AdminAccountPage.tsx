import { useCallback, useEffect, useState, type FormEvent } from "react";
import { useParams } from "react-router-dom";
import { api, ApiError } from "../api";
import { AccessBadge, AppHeader, ErrorBanner, Field, Loading } from "../components/ui";
import { extendAccess, formatDate, formatMoney, parseAmount, todayIso, whatsappLink } from "../format";
import type { AccountDetail } from "../types";

const QUICK_DAYS = [30, 90, 180, 365];

export function AdminAccountPage() {
  const { id = "" } = useParams();
  const [detail, setDetail] = useState<AccountDetail | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [days, setDays] = useState("30");
  const [amount, setAmount] = useState("");
  const [note, setNote] = useState("");
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({});
  const [busy, setBusy] = useState(false);
  const [saved, setSaved] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      setDetail(await api.adminAccount(id));
    } catch (e) {
      setError(e instanceof ApiError && e.status === 404 ? "Conta não encontrada." : "Erro ao carregar a conta.");
    }
  }, [id]);

  useEffect(() => {
    void load();
  }, [load]);

  if (!detail) {
    return (
      <>
        <AppHeader title="Conta" back="/admin" />
        <main className="page">{error ? <ErrorBanner message={error} /> : <Loading />}</main>
      </>
    );
  }

  const { account, grants } = detail;
  const dayCount = Number.parseInt(days, 10);
  const preview = Number.isFinite(dayCount) && dayCount > 0 ? extendAccess(account.accessUntil, dayCount, todayIso()) : null;
  const firstName = account.name.split(" ")[0];
  const whatsapp = account.phone
    ? whatsappLink(
        account.phone,
        account.state === "Pending"
          ? `Olá, ${firstName}! Recebi sua solicitação de acesso ao app de Cobranças.`
          : `Olá, ${firstName}! Sobre o seu acesso ao app de Cobranças:`,
      )
    : null;

  const grant = async (event: FormEvent) => {
    event.preventDefault();
    const paid = amount.trim() === "" ? null : parseAmount(amount);
    if (amount.trim() !== "" && paid === null) {
      setFieldErrors({ amountPaid: ["Valor inválido."] });
      return;
    }

    setBusy(true);
    setError(null);
    setSaved(null);
    try {
      const updated = await api.adminGrant(id, { days: Number.isFinite(dayCount) ? dayCount : null, amountPaid: paid, note });
      setDetail(updated);
      setFieldErrors({});
      setAmount("");
      setNote("");
      setSaved(`Acesso liberado até ${formatDate(updated.account.accessUntil!)}.`);
    } catch (e) {
      if (e instanceof ApiError) {
        setFieldErrors(e.fieldErrors);
        setError(e.message);
      } else {
        setError("Erro inesperado.");
      }
    } finally {
      setBusy(false);
    }
  };

  const act = async (action: "suspend" | "reactivate" | "reject", confirmText: string) => {
    if (!window.confirm(confirmText)) return;
    setBusy(true);
    setSaved(null);
    try {
      setDetail(await api.adminAction(id, action));
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Erro inesperado.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <>
      <AppHeader title={account.name} back="/admin" />
      <main className="page">
        <ErrorBanner message={error} />
        {saved && (
          <p className="success-banner" role="status">
            {saved}
          </p>
        )}

        <section className="card account-card">
          <div className="card-top">
            <div>
              <strong>{account.email}</strong>
              <span className="muted">
                Plano {account.plan} · solicitado em {formatDate(account.createdAt.slice(0, 10))}
              </span>
            </div>
            <AccessBadge state={account.state} />
          </div>
          <p className="card-text">
            {account.accessUntil ? (
              <>
                Acesso até <strong>{formatDate(account.accessUntil)}</strong>
                {account.daysLeft !== null && account.daysLeft >= 0 && ` (${account.daysLeft} dias)`}
              </>
            ) : (
              "Acesso ainda não liberado."
            )}
          </p>
          {account.adminNote && <p className="card-text muted">Observação: {account.adminNote}</p>}
          {whatsapp && (
            <a href={whatsapp} target="_blank" rel="noopener noreferrer" className="button button-ghost whatsapp-button">
              Conversar no WhatsApp · {account.phone}
            </a>
          )}
        </section>

        <h2 className="section-heading">{account.state === "Pending" ? "Aprovar acesso" : "Liberar mais dias"}</h2>
        <form className="form card" onSubmit={grant} noValidate>
          <div className="chips" role="group" aria-label="Dias">
            {QUICK_DAYS.map((d) => (
              <button
                key={d}
                type="button"
                className={`chip${days === String(d) ? " chip-selected" : ""}`}
                onClick={() => setDays(String(d))}
              >
                {d} dias
              </button>
            ))}
          </div>
          <div className="field-row">
            <Field label="Dias" error={fieldErrors.days}>
              {(fieldId, describedBy) => (
                <input
                  id={fieldId}
                  type="number"
                  inputMode="numeric"
                  min={1}
                  aria-describedby={describedBy}
                  value={days}
                  onChange={(e) => setDays(e.target.value)}
                />
              )}
            </Field>
            <Field label="Valor pago (R$)" error={fieldErrors.amountPaid}>
              {(fieldId, describedBy) => (
                <input
                  id={fieldId}
                  inputMode="decimal"
                  aria-describedby={describedBy}
                  value={amount}
                  onChange={(e) => setAmount(e.target.value)}
                  placeholder="Opcional"
                />
              )}
            </Field>
          </div>
          <Field label="Observação (opcional)" error={fieldErrors.note}>
            {(fieldId, describedBy) => (
              <input
                id={fieldId}
                aria-describedby={describedBy}
                value={note}
                onChange={(e) => setNote(e.target.value)}
                placeholder="Ex.: Pix recebido em 24/11"
              />
            )}
          </Field>
          {preview && (
            <p className="plan-preview" aria-live="polite">
              Novo vencimento: {formatDate(preview)}
            </p>
          )}
          <button type="submit" className="button button-primary" disabled={busy}>
            {account.state === "Pending" ? "Aprovar e liberar acesso" : "Liberar dias"}
          </button>
        </form>

        <div className="admin-actions">
          {account.status === "Pending" && (
            <button
              type="button"
              className="button button-danger-ghost"
              disabled={busy}
              onClick={() => act("reject", `Recusar a solicitação de ${account.name}?`)}
            >
              Recusar solicitação
            </button>
          )}
          {account.status === "Active" && (
            <button
              type="button"
              className="button button-danger-ghost"
              disabled={busy}
              onClick={() => act("suspend", `Suspender o acesso de ${account.name}? O bloqueio é imediato.`)}
            >
              Suspender acesso
            </button>
          )}
          {account.status === "Suspended" && (
            <button
              type="button"
              className="button button-ghost"
              disabled={busy}
              onClick={() => act("reactivate", `Reativar o acesso de ${account.name}?`)}
            >
              Reativar acesso
            </button>
          )}
        </div>

        <h2 className="section-heading">Histórico de liberações</h2>
        {grants.length === 0 ? (
          <p className="empty">Nenhuma liberação ainda.</p>
        ) : (
          <ul className="list">
            {grants.map((g) => (
              <li key={g.id} className="card">
                <div className="card-top">
                  <div>
                    <strong>
                      +{g.days} dias{g.amountPaid !== null && ` · ${formatMoney(g.amountPaid)}`}
                    </strong>
                    <span className="muted">
                      {formatDate(g.grantedAt.slice(0, 10))} · até {formatDate(g.newUntil)}
                    </span>
                  </div>
                </div>
                {g.note && <p className="card-text muted">{g.note}</p>}
              </li>
            ))}
          </ul>
        )}
      </main>
    </>
  );
}
