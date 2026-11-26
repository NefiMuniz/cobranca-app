import { useCallback, useEffect, useMemo, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { api, ApiError } from "../api";
import { ChargeForm } from "../components/ChargeForm";
import { PaymentForm } from "../components/PaymentForm";
import { AppHeader, ErrorBanner, Loading, Sheet, StatusBadge } from "../components/ui";
import { formatDate, formatMoney, formatMonth, whatsappLink } from "../format";
import type { Charge, ChargeInput, ClientDetail, PaymentInput } from "../types";

type Panel = { type: "charge"; charge?: Charge } | { type: "payment" } | null;
type Tab = "charges" | "payments";

export function ClientPage() {
  const { id = "" } = useParams();
  const [detail, setDetail] = useState<ClientDetail | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [panel, setPanel] = useState<Panel>(null);
  const [tab, setTab] = useState<Tab>("charges");
  const [showPaid, setShowPaid] = useState(false);

  const load = useCallback(async () => {
    try {
      setDetail(await api.getClient(id));
      setError(null);
    } catch (e) {
      setError(e instanceof ApiError && e.status === 404 ? "Cliente não encontrado." : "Erro ao carregar o cliente.");
    }
  }, [id]);

  useEffect(() => {
    void load();
  }, [load]);

  const closePanel = useCallback(() => setPanel(null), []);

  const lines = useMemo(() => {
    const all = [...(detail?.statement.lines ?? [])].reverse(); // newest first
    return showPaid ? all : all.filter((l) => l.status !== "Paid");
  }, [detail, showPaid]);

  const openLines = useMemo(() => (detail?.statement.lines ?? []).filter((l) => l.remaining > 0), [detail]);
  const suggestions = useMemo(
    () => [...new Set((detail?.statement.lines ?? []).map((l) => l.charge.serviceName))],
    [detail],
  );
  const chargeNames = useMemo(
    () => new Map((detail?.statement.lines ?? []).map((l) => [l.charge.id, `${l.charge.serviceName} · ${formatMonth(l.charge.reference)}`])),
    [detail],
  );

  const saveCharge = async (input: ChargeInput) => {
    if (panel?.type === "charge" && panel.charge) await api.updateCharge(id, panel.charge.id, input);
    else await api.createCharge(id, input);
    setPanel(null);
    await load();
  };

  const savePayment = async (input: PaymentInput) => {
    await api.createPayment(id, input);
    setPanel(null);
    await load();
  };

  const removeCharge = async (charge: Charge) => {
    if (!window.confirm(`Excluir a cobrança "${charge.serviceName}" de ${formatMonth(charge.reference)}?`)) return;
    await run(() => api.deleteCharge(id, charge.id));
  };

  const removePayment = async (paymentId: string) => {
    if (!window.confirm("Excluir este pagamento?")) return;
    await run(() => api.deletePayment(id, paymentId));
  };

  const run = async (action: () => Promise<void>) => {
    try {
      await action();
      await load();
    } catch (e) {
      setError(e instanceof ApiError ? e.message : "Erro inesperado.");
    }
  };

  if (!detail) {
    return (
      <>
        <AppHeader title="Cliente" back="/" />
        <main className="page">{error ? <ErrorBanner message={error} /> : <Loading />}</main>
      </>
    );
  }

  const { client, statement, payments } = detail;

  return (
    <>
      <AppHeader
        title={client.name}
        back="/"
        action={
          <Link to={`/clientes/${client.id}/editar`} className="button button-ghost button-small">
            Editar
          </Link>
        }
      />
      <main className="page page-with-actions">
        <ErrorBanner message={error} />

        {(client.phone || client.notes) && (
          <section className="client-info">
            {client.phone && <PhoneLink phone={client.phone} />}
            {client.notes && <p className="muted">{client.notes}</p>}
          </section>
        )}

        <section className="totals" aria-label="Saldo do cliente">
          <div className={`total-card${statement.overdueAmount > 0 ? " total-card-danger" : ""}`}>
            <span>Em aberto</span>
            <strong>{formatMoney(statement.outstanding)}</strong>
            {statement.overdueAmount > 0 && <small>{formatMoney(statement.overdueAmount)} vencido</small>}
          </div>
          <div className="total-card">
            <span>Total pago</span>
            <strong>{formatMoney(statement.totalPaid)}</strong>
            <small>de {formatMoney(statement.totalCharged)}</small>
          </div>
          {statement.credit > 0 && (
            <div className="total-card total-card-success">
              <span>Crédito</span>
              <strong>{formatMoney(statement.credit)}</strong>
            </div>
          )}
        </section>

        <div className="tabs" role="tablist">
          <button type="button" role="tab" aria-selected={tab === "charges"} onClick={() => setTab("charges")}>
            Cobranças ({statement.lines.length})
          </button>
          <button type="button" role="tab" aria-selected={tab === "payments"} onClick={() => setTab("payments")}>
            Pagamentos ({payments.length})
          </button>
        </div>

        {tab === "charges" && (
          <>
            <label className="toggle">
              <input type="checkbox" checked={showPaid} onChange={(e) => setShowPaid(e.target.checked)} />
              Mostrar cobranças pagas
            </label>
            {lines.length === 0 ? (
              <p className="empty">{statement.lines.length === 0 ? "Nenhuma cobrança lançada." : "Tudo pago por aqui."}</p>
            ) : (
              <ul className="list">
                {lines.map((line) => (
                  <li key={line.charge.id} className="card">
                    <div className="card-top">
                      <div>
                        <strong>{line.charge.serviceName}</strong>
                        <span className="muted">
                          {formatMonth(line.charge.reference)} · vence {formatDate(line.charge.dueDate)}
                          {line.charge.kind === "Recurring" && " · mensalidade"}
                        </span>
                      </div>
                      <StatusBadge line={line} />
                    </div>
                    {line.charge.description && <p className="card-text">{line.charge.description}</p>}
                    <div className="card-bottom">
                      <span>
                        {formatMoney(line.charge.amount)}
                        {line.remaining > 0 && line.paid > 0 && <span className="muted"> · falta {formatMoney(line.remaining)}</span>}
                      </span>
                      <span className="card-actions">
                        <button type="button" className="link-button" onClick={() => setPanel({ type: "charge", charge: line.charge })}>
                          Editar
                        </button>
                        <button type="button" className="link-button link-danger" onClick={() => removeCharge(line.charge)}>
                          Excluir
                        </button>
                      </span>
                    </div>
                  </li>
                ))}
              </ul>
            )}
          </>
        )}

        {tab === "payments" &&
          (payments.length === 0 ? (
            <p className="empty">Nenhum pagamento registrado.</p>
          ) : (
            <ul className="list">
              {payments.map((payment) => (
                <li key={payment.id} className="card">
                  <div className="card-top">
                    <div>
                      <strong>{formatMoney(payment.amount)}</strong>
                      <span className="muted">
                        {formatDate(payment.paidOn)}
                        {payment.note && ` · ${payment.note}`}
                      </span>
                    </div>
                    <button type="button" className="link-button link-danger" onClick={() => removePayment(payment.id)}>
                      Excluir
                    </button>
                  </div>
                  <p className="card-text muted">
                    {payment.chargeId
                      ? `Abatido de: ${chargeNames.get(payment.chargeId) ?? "cobrança excluída"}`
                      : "Abatido das cobranças mais antigas"}
                  </p>
                </li>
              ))}
            </ul>
          ))}
      </main>

      <div className="action-bar">
        <button type="button" className="button button-ghost" onClick={() => setPanel({ type: "charge" })}>
          Nova cobrança
        </button>
        <button type="button" className="button button-primary" onClick={() => setPanel({ type: "payment" })}>
          Registrar pagamento
        </button>
      </div>

      {panel?.type === "charge" && (
        <Sheet title={panel.charge ? "Editar cobrança" : "Nova cobrança"} onClose={closePanel}>
          <ChargeForm initial={panel.charge} suggestions={suggestions} onSubmit={saveCharge} onCancel={closePanel} />
        </Sheet>
      )}
      {panel?.type === "payment" && (
        <Sheet title="Registrar pagamento" onClose={closePanel}>
          <PaymentForm openLines={openLines} onSubmit={savePayment} onCancel={closePanel} />
        </Sheet>
      )}
    </>
  );
}

/** Opens the client's WhatsApp chat; falls back to plain text when the number is incomplete. */
function PhoneLink({ phone }: { phone: string }) {
  const href = whatsappLink(phone);
  if (!href) return <span>{phone}</span>;
  return (
    <a href={href} target="_blank" rel="noopener noreferrer" className="whatsapp-link">
      {phone} · WhatsApp
    </a>
  );
}
