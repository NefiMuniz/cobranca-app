import { useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { api, ApiError } from "../api";
import { AccessBadge, AppHeader, ErrorBanner, Loading } from "../components/ui";
import { formatDate } from "../format";
import type { AccountView } from "../types";

type Filter = "pending" | "active" | "all";

export function AdminAccountsPage() {
  const [accounts, setAccounts] = useState<AccountView[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [filter, setFilter] = useState<Filter>("pending");

  useEffect(() => {
    api
      .adminAccounts()
      .then((list) => {
        setAccounts(list);
        if (!list.some((a) => a.state === "Pending")) setFilter("all");
      })
      .catch((e) => setError(e instanceof ApiError ? e.message : "Erro ao carregar as contas."));
  }, []);

  const counts = useMemo(
    () => ({
      pending: accounts?.filter((a) => a.state === "Pending").length ?? 0,
      active: accounts?.filter((a) => a.state === "Active").length ?? 0,
      all: accounts?.length ?? 0,
    }),
    [accounts],
  );

  const visible = (accounts ?? []).filter((a) =>
    filter === "pending" ? a.state === "Pending" : filter === "active" ? a.state === "Active" : true,
  );

  return (
    <>
      <AppHeader title="Contas" back="/" />
      <main className="page">
        <ErrorBanner message={error} />
        {!accounts && !error && <Loading />}
        {accounts && (
          <>
            <div className="tabs tabs-3" role="tablist">
              <button type="button" role="tab" aria-selected={filter === "pending"} onClick={() => setFilter("pending")}>
                Pendentes ({counts.pending})
              </button>
              <button type="button" role="tab" aria-selected={filter === "active"} onClick={() => setFilter("active")}>
                Ativas ({counts.active})
              </button>
              <button type="button" role="tab" aria-selected={filter === "all"} onClick={() => setFilter("all")}>
                Todas ({counts.all})
              </button>
            </div>

            {visible.length === 0 ? (
              <p className="empty">{filter === "pending" ? "Nenhuma solicitação aguardando." : "Nenhuma conta aqui."}</p>
            ) : (
              <ul className="list">
                {visible.map((account) => (
                  <li key={account.id}>
                    <Link to={`/admin/contas/${account.id}`} className="list-item">
                      <div>
                        <strong>{account.name}</strong>
                        <span className="muted">{account.email}</span>
                        <span className="muted">
                          {account.accessUntil
                            ? `Acesso até ${formatDate(account.accessUntil)}`
                            : `Solicitado em ${formatDate(account.createdAt.slice(0, 10))}`}
                        </span>
                      </div>
                      <div className="list-item-amount">
                        <AccessBadge state={account.state} />
                        {account.state === "Active" && account.daysLeft !== null && (
                          <span className={account.daysLeft <= 7 ? "text-danger" : "muted"}>
                            {account.daysLeft === 0 ? "vence hoje" : `${account.daysLeft} dias`}
                          </span>
                        )}
                      </div>
                    </Link>
                  </li>
                ))}
              </ul>
            )}
          </>
        )}
      </main>
    </>
  );
}
