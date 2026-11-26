import { useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { api, ApiError } from "../api";
import { useAuth } from "../auth";
import { AppHeader, ErrorBanner, Loading } from "../components/ui";
import { formatDate, formatMoney, whatsappLink } from "../format";
import type { Dashboard, Me } from "../types";

const SUPPORT_WHATSAPP = import.meta.env.VITE_SUPPORT_WHATSAPP ?? "";
const EXPIRY_WARNING_DAYS = 7;

export function DashboardPage() {
  const { logout, role } = useAuth();
  const [data, setData] = useState<Dashboard | null>(null);
  const [me, setMe] = useState<Me | null>(null);
  const [pendingAccounts, setPendingAccounts] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [query, setQuery] = useState("");

  useEffect(() => {
    let active = true;
    api
      .dashboard()
      .then((d) => active && setData(d))
      .catch((e) => active && setError(e instanceof ApiError ? e.message : "Erro ao carregar."));
    if (role === "admin") {
      api
        .adminAccounts()
        .then((accounts) => active && setPendingAccounts(accounts.filter((a) => a.state === "Pending").length))
        .catch(() => {});
    } else {
      api
        .me()
        .then((m) => active && setMe(m))
        .catch(() => {});
    }
    return () => {
      active = false;
    };
  }, [role]);

  const clients = useMemo(() => {
    const term = query.trim().toLocaleLowerCase("pt-BR");
    return (data?.clients ?? []).filter((c) => c.name.toLocaleLowerCase("pt-BR").includes(term));
  }, [data, query]);

  return (
    <>
      <AppHeader
        title="Cobranças"
        action={
          <>
            {role === "admin" && (
              <Link to="/admin" className="button button-ghost button-small">
                Contas{pendingAccounts > 0 && <span className="count-badge">{pendingAccounts}</span>}
              </Link>
            )}
            <button type="button" className="button button-ghost button-small" onClick={() => logout()}>
              Sair
            </button>
          </>
        }
      />
      <main className="page">
        <ErrorBanner message={error} />
        {me?.daysLeft != null && me.daysLeft <= EXPIRY_WARNING_DAYS && <ExpiryBanner me={me} />}
        {!data && !error && <Loading />}
        {data && (
          <>
            <section className="totals" aria-label="Resumo">
              <div className="total-card">
                <span>A receber</span>
                <strong>{formatMoney(data.totalOutstanding)}</strong>
              </div>
              <div className={`total-card${data.totalOverdue > 0 ? " total-card-danger" : ""}`}>
                <span>Vencido</span>
                <strong>{formatMoney(data.totalOverdue)}</strong>
                <small>
                  {data.overdueCharges} {data.overdueCharges === 1 ? "cobrança" : "cobranças"}
                </small>
              </div>
              <div className="total-card">
                <span>Recebido no mês</span>
                <strong>{formatMoney(data.receivedThisMonth)}</strong>
              </div>
            </section>

            <div className="section-title">
              <h2>Clientes</h2>
              <span className="muted">{data.clients.length}</span>
            </div>

            {data.clients.length > 3 && (
              <input
                type="search"
                className="search"
                placeholder="Buscar cliente"
                aria-label="Buscar cliente"
                value={query}
                onChange={(e) => setQuery(e.target.value)}
              />
            )}

            {data.clients.length === 0 ? (
              <p className="empty">Nenhum cliente ainda. Cadastre o primeiro para começar.</p>
            ) : (
              <ul className="list">
                {clients.map((client) => (
                  <li key={client.id}>
                    <Link to={`/clientes/${client.id}`} className="list-item">
                      <div>
                        <strong>{client.name}</strong>
                        <span className="muted">
                          {client.outstanding === 0
                            ? "Em dia"
                            : client.nextDueDate
                              ? `Próximo vencimento ${formatDate(client.nextDueDate)}`
                              : `${client.openCharges} em aberto`}
                        </span>
                      </div>
                      <div className="list-item-amount">
                        <strong className={client.overdueAmount > 0 ? "text-danger" : undefined}>
                          {formatMoney(client.outstanding)}
                        </strong>
                        {client.overdueAmount > 0 && (
                          <span className="badge badge-danger">{formatMoney(client.overdueAmount)} vencido</span>
                        )}
                        {client.credit > 0 && <span className="badge badge-success">Crédito {formatMoney(client.credit)}</span>}
                      </div>
                    </Link>
                  </li>
                ))}
              </ul>
            )}
          </>
        )}
      </main>
      <Link to="/clientes/novo" className="fab">
        + Cliente
      </Link>
    </>
  );
}

function ExpiryBanner({ me }: { me: Me }) {
  const support = SUPPORT_WHATSAPP
    ? whatsappLink(SUPPORT_WHATSAPP, `Olá! Quero renovar meu acesso ao app de Cobranças (${me.email ?? me.name}).`)
    : null;
  const when = me.daysLeft === 0 ? "hoje" : me.daysLeft === 1 ? "amanhã" : `em ${me.daysLeft} dias`;
  return (
    <p className="access-banner" role="status">
      Seu acesso vence {when} ({formatDate(me.accessUntil!)}).{" "}
      {support ? (
        <a href={support} target="_blank" rel="noopener noreferrer">
          Renovar pelo WhatsApp
        </a>
      ) : (
        "Fale com o suporte para renovar."
      )}
    </p>
  );
}
