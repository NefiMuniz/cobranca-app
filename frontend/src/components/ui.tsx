import { useEffect, useId, type ReactNode } from "react";
import { Link } from "react-router-dom";
import { statusLabel } from "../format";
import type { AccessState, ChargeLine } from "../types";

export function AppHeader({ title, back, action }: { title: string; back?: string; action?: ReactNode }) {
  return (
    <header className="app-header">
      {back ? (
        <Link to={back} className="icon-button" aria-label="Voltar">
          ←
        </Link>
      ) : (
        <span className="brand-mark" aria-hidden="true">
          R$
        </span>
      )}
      <h1>{title}</h1>
      <div className="app-header-action">{action}</div>
    </header>
  );
}

export function StatusBadge({ line }: { line: Pick<ChargeLine, "status" | "isOverdue"> }) {
  const tone = line.isOverdue ? "danger" : line.status === "Paid" ? "success" : line.status === "Partial" ? "warning" : "neutral";
  return <span className={`badge badge-${tone}`}>{statusLabel(line)}</span>;
}

export function Field({
  label,
  error,
  hint,
  children,
}: {
  label: string;
  error?: string[];
  hint?: string;
  children: (id: string, describedBy: string | undefined) => ReactNode;
}) {
  const id = useId();
  const messageId = `${id}-message`;
  const message = error?.[0] ?? hint;
  return (
    <div className={`field${error ? " field-invalid" : ""}`}>
      <label htmlFor={id}>{label}</label>
      {children(id, message ? messageId : undefined)}
      {message && (
        <p id={messageId} className={error ? "field-error" : "field-hint"}>
          {message}
        </p>
      )}
    </div>
  );
}

/** Bottom sheet on phones, centered dialog on larger screens. */
export function Sheet({ title, onClose, children }: { title: string; onClose: () => void; children: ReactNode }) {
  const titleId = useId();

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.key === "Escape") onClose();
    };
    document.addEventListener("keydown", onKey);
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    return () => {
      document.removeEventListener("keydown", onKey);
      document.body.style.overflow = previousOverflow;
    };
  }, [onClose]);

  return (
    <div className="sheet-backdrop" onClick={onClose}>
      <div
        className="sheet"
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        onClick={(event) => event.stopPropagation()}
      >
        <div className="sheet-header">
          <h2 id={titleId}>{title}</h2>
          <button type="button" className="icon-button" onClick={onClose} aria-label="Fechar">
            ×
          </button>
        </div>
        {children}
      </div>
    </div>
  );
}

export function ErrorBanner({ message }: { message: string | null }) {
  if (!message) return null;
  return (
    <p className="error-banner" role="alert">
      {message}
    </p>
  );
}

export function Loading() {
  return (
    <p className="loading" role="status">
      Carregando…
    </p>
  );
}

const ACCESS_LABELS: Record<AccessState, [string, string]> = {
  Pending: ["Aguardando", "warning"],
  Active: ["Ativa", "success"],
  Expired: ["Vencida", "danger"],
  Suspended: ["Suspensa", "danger"],
  Rejected: ["Recusada", "neutral"],
};

export function AccessBadge({ state }: { state: AccessState }) {
  const [label, tone] = ACCESS_LABELS[state];
  return <span className={`badge badge-${tone}`}>{label}</span>;
}
