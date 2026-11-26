import { useState, type FormEvent } from "react";
import { ApiError } from "../api";
import {
  addMonths,
  amountToInput,
  defaultDueDate,
  formatMoney,
  formatMonth,
  fromMonthInput,
  monthsBetween,
  parseAmount,
  splitInstallments,
  toMonthInput,
  todayIso,
} from "../format";
import type { Charge, ChargeInput, ChargeKind } from "../types";
import { ErrorBanner, Field } from "./ui";

const MAX_CHARGES = 60;

interface Props {
  initial?: Charge;
  /** Service names already used for this client, offered as suggestions (free text is still allowed). */
  suggestions: string[];
  onSubmit: (input: ChargeInput) => Promise<void>;
  onCancel: () => void;
}

export function ChargeForm({ initial, suggestions, onSubmit, onCancel }: Props) {
  const isNew = !initial;
  const startMonth = initial ? toMonthInput(initial.reference) : todayIso().slice(0, 7);
  const [serviceName, setServiceName] = useState(initial?.serviceName ?? "");
  const [description, setDescription] = useState(initial?.description ?? "");
  const [kind, setKind] = useState<ChargeKind>(initial?.kind ?? "OneOff");
  const [month, setMonth] = useState(startMonth);
  const [dueDate, setDueDate] = useState(initial?.dueDate ?? defaultDueDate(startMonth));
  const [dueDateTouched, setDueDateTouched] = useState(Boolean(initial));
  const [amount, setAmount] = useState(initial ? amountToInput(initial.amount) : "");

  // Creation-only plan options.
  const [repeat, setRepeat] = useState(false);
  const [repeatUntil, setRepeatUntil] = useState(addMonths(startMonth, 5));
  const [split, setSplit] = useState(false);
  const [installments, setInstallments] = useState("2");

  const [errors, setErrors] = useState<Record<string, string[]>>({});
  const [message, setMessage] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  const repeating = isNew && kind === "Recurring" && repeat;
  const splitting = isNew && kind === "OneOff" && split;
  const parsedAmount = parseAmount(amount);
  const installmentCount = Number.parseInt(installments, 10);
  const repeatCount = month && repeatUntil ? monthsBetween(month, repeatUntil) : 0;
  const chargeCount = repeating ? repeatCount : splitting ? installmentCount : 1;

  const changeMonth = (value: string) => {
    setMonth(value);
    if (!dueDateTouched && value) setDueDate(defaultDueDate(value));
    if (value && repeatUntil && monthsBetween(value, repeatUntil) < 1) setRepeatUntil(value);
  };

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (amount.trim() !== "" && parsedAmount === null) {
      setErrors({ amount: ["Valor inválido. Exemplo: 600,00"] });
      return;
    }

    setSaving(true);
    setMessage(null);
    try {
      await onSubmit({
        serviceName,
        description,
        kind,
        reference: month ? fromMonthInput(month) : null,
        dueDate: dueDate || null,
        amount: parsedAmount,
        repeatUntil: repeating && repeatUntil ? fromMonthInput(repeatUntil) : null,
        installments: splitting ? (Number.isNaN(installmentCount) ? 0 : installmentCount) : null,
      });
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

      <Field label="Serviço" error={errors.serviceName}>
        {(id, describedBy) => (
          <>
            <input
              id={id}
              aria-describedby={describedBy}
              list={`${id}-suggestions`}
              value={serviceName}
              onChange={(e) => setServiceName(e.target.value)}
              placeholder="Ex.: Honorários contábeis"
              autoComplete="off"
              required
            />
            <datalist id={`${id}-suggestions`}>
              {suggestions.map((s) => (
                <option key={s} value={s} />
              ))}
            </datalist>
          </>
        )}
      </Field>

      <Field label="Descrição (opcional)" error={errors.description} hint="Detalhes do que foi feito neste serviço.">
        {(id, describedBy) => (
          <textarea
            id={id}
            aria-describedby={describedBy}
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            rows={3}
          />
        )}
      </Field>

      <Field label="Tipo">
        {(id) => (
          <select id={id} value={kind} onChange={(e) => setKind(e.target.value as ChargeKind)}>
            <option value="OneOff">Serviço avulso</option>
            <option value="Recurring">Mensalidade</option>
          </select>
        )}
      </Field>

      <div className="field-row">
        <Field label={repeating || splitting ? "Primeira referência" : "Referência"} error={errors.reference}>
          {(id, describedBy) => (
            <input
              id={id}
              type="month"
              aria-describedby={describedBy}
              value={month}
              onChange={(e) => changeMonth(e.target.value)}
            />
          )}
        </Field>
        <Field label={repeating || splitting ? "Primeiro vencimento" : "Vencimento"} error={errors.dueDate}>
          {(id, describedBy) => (
            <input
              id={id}
              type="date"
              aria-describedby={describedBy}
              value={dueDate}
              onChange={(e) => {
                setDueDateTouched(true);
                setDueDate(e.target.value);
              }}
            />
          )}
        </Field>
      </div>

      <Field label={splitting ? "Valor total (R$)" : repeating ? "Valor mensal (R$)" : "Valor (R$)"} error={errors.amount}>
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

      {isNew && kind === "Recurring" && (
        <fieldset className="plan">
          <label className="toggle">
            <input type="checkbox" checked={repeat} onChange={(e) => setRepeat(e.target.checked)} />
            Repetir todo mês
          </label>
          {repeat && (
            <Field label="Repetir até" error={errors.repeatUntil}>
              {(id, describedBy) => (
                <input
                  id={id}
                  type="month"
                  min={month}
                  aria-describedby={describedBy}
                  value={repeatUntil}
                  onChange={(e) => setRepeatUntil(e.target.value)}
                />
              )}
            </Field>
          )}
          {repeat && repeatCount >= 1 && repeatCount <= MAX_CHARGES && (
            <p className="plan-preview" aria-live="polite">
              {repeatCount} {repeatCount === 1 ? "mensalidade" : "mensalidades"} de {formatMonth(fromMonthInput(month))} a{" "}
              {formatMonth(fromMonthInput(repeatUntil))}
              {parsedAmount !== null && `, ${formatMoney(parsedAmount)} cada`}
            </p>
          )}
        </fieldset>
      )}

      {isNew && kind === "OneOff" && (
        <fieldset className="plan">
          <label className="toggle">
            <input type="checkbox" checked={split} onChange={(e) => setSplit(e.target.checked)} />
            Parcelado
          </label>
          {split && (
            <Field label="Número de parcelas" error={errors.installments}>
              {(id, describedBy) => (
                <input
                  id={id}
                  type="number"
                  inputMode="numeric"
                  min={2}
                  max={MAX_CHARGES}
                  aria-describedby={describedBy}
                  value={installments}
                  onChange={(e) => setInstallments(e.target.value)}
                />
              )}
            </Field>
          )}
          {split && installmentCount >= 2 && installmentCount <= MAX_CHARGES && parsedAmount !== null && (
            <InstallmentPreview total={parsedAmount} count={installmentCount} firstMonth={month} />
          )}
        </fieldset>
      )}

      <div className="form-actions">
        <button type="button" className="button button-ghost" onClick={onCancel}>
          Cancelar
        </button>
        <button type="submit" className="button button-primary" disabled={saving}>
          {saving ? "Salvando…" : chargeCount > 1 ? `Gerar ${chargeCount} cobranças` : "Salvar cobrança"}
        </button>
      </div>
    </form>
  );
}

function InstallmentPreview({ total, count, firstMonth }: { total: number; count: number; firstMonth: string }) {
  const parts = splitInstallments(total, count);
  const first = parts[0];
  const last = parts[parts.length - 1];
  const period = `${formatMonth(fromMonthInput(firstMonth))} a ${formatMonth(fromMonthInput(addMonths(firstMonth, count - 1)))}`;
  return (
    <p className="plan-preview" aria-live="polite">
      {first === last
        ? `${count} parcelas de ${formatMoney(first)}`
        : `${count - 1} parcelas de ${formatMoney(first)} e a última de ${formatMoney(last)}`}
      , de {period}
    </p>
  );
}
