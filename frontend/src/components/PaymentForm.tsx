import { useState, type FormEvent } from "react";
import { ApiError } from "../api";
import { amountToInput, formatMoney, formatMonth, parseAmount, todayIso } from "../format";
import type { ChargeLine, PaymentInput } from "../types";
import { ErrorBanner, Field } from "./ui";

interface Props {
  openLines: ChargeLine[];
  onSubmit: (input: PaymentInput) => Promise<void>;
  onCancel: () => void;
}

export function PaymentForm({ openLines, onSubmit, onCancel }: Props) {
  const [amount, setAmount] = useState("");
  const [paidOn, setPaidOn] = useState(todayIso());
  const [chargeId, setChargeId] = useState("");
  const [note, setNote] = useState("");
  const [errors, setErrors] = useState<Record<string, string[]>>({});
  const [message, setMessage] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  const chooseCharge = (id: string) => {
    setChargeId(id);
    const line = openLines.find((l) => l.charge.id === id);
    if (line && amount.trim() === "") setAmount(amountToInput(line.remaining));
  };

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    const parsedAmount = parseAmount(amount);
    if (amount.trim() !== "" && parsedAmount === null) {
      setErrors({ amount: ["Valor inválido. Exemplo: 500,00"] });
      return;
    }

    setSaving(true);
    setMessage(null);
    try {
      await onSubmit({ amount: parsedAmount, paidOn: paidOn || null, chargeId: chargeId || null, note });
    } catch (error) {
      if (error instanceof ApiError) {
        setErrors(error.fieldErrors);
        setMessage(error.message);
      } else {
        setMessage("Erro inesperado.");
      }
    } finally {
      setSaving(false);
    }
  };

  return (
    <form onSubmit={submit} className="form" noValidate>
      <ErrorBanner message={message} />

      <Field label="Abater de" error={errors.chargeId} hint="Sem escolha, o valor quita as cobranças mais antigas primeiro.">
        {(id, describedBy) => (
          <select id={id} aria-describedby={describedBy} value={chargeId} onChange={(e) => chooseCharge(e.target.value)}>
            <option value="">Cobranças mais antigas</option>
            {openLines.map((line) => (
              <option key={line.charge.id} value={line.charge.id}>
                {line.charge.serviceName} · {formatMonth(line.charge.reference)} · falta {formatMoney(line.remaining)}
              </option>
            ))}
          </select>
        )}
      </Field>

      <div className="field-row">
        <Field label="Valor (R$)" error={errors.amount}>
          {(id, describedBy) => (
            <input
              id={id}
              inputMode="decimal"
              aria-describedby={describedBy}
              value={amount}
              onChange={(e) => setAmount(e.target.value)}
              placeholder="0,00"
            />
          )}
        </Field>
        <Field label="Data" error={errors.paidOn}>
          {(id, describedBy) => (
            <input id={id} type="date" aria-describedby={describedBy} value={paidOn} onChange={(e) => setPaidOn(e.target.value)} />
          )}
        </Field>
      </div>

      <Field label="Observação (opcional)" error={errors.note}>
        {(id, describedBy) => (
          <input
            id={id}
            aria-describedby={describedBy}
            value={note}
            onChange={(e) => setNote(e.target.value)}
            placeholder="Ex.: Pix, dinheiro, transferência"
          />
        )}
      </Field>

      <div className="form-actions">
        <button type="button" className="button button-ghost" onClick={onCancel}>
          Cancelar
        </button>
        <button type="submit" className="button button-primary" disabled={saving}>
          {saving ? "Salvando…" : "Registrar pagamento"}
        </button>
      </div>
    </form>
  );
}
