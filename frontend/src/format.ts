import type { ChargeLine } from "./types";

const money = new Intl.NumberFormat("pt-BR", { style: "currency", currency: "BRL" });
const MONTHS = ["jan", "fev", "mar", "abr", "mai", "jun", "jul", "ago", "set", "out", "nov", "dez"];

export function formatMoney(value: number): string {
  return money.format(value);
}

/** "2026-11-05" → "05/11/2026". String-based so the browser time zone can never shift the day. */
export function formatDate(iso: string): string {
  const [year, month, day] = iso.split("-");
  return `${day}/${month}/${year}`;
}

/** "2026-11-01" → "nov/2026" */
export function formatMonth(iso: string): string {
  const [year, month] = iso.split("-");
  return `${MONTHS[Number(month) - 1]}/${year}`;
}

/** Value for <input type="month">: "2026-11-01" → "2026-11" */
export function toMonthInput(iso: string): string {
  return iso.slice(0, 7);
}

/** "2026-11" → "2026-11-01" */
export function fromMonthInput(month: string): string {
  return `${month}-01`;
}

/** Default due date: 5th of the month after the reference month ("2026-11" → "2026-12-05"). */
export function defaultDueDate(month: string): string {
  const [year, m] = month.split("-").map(Number);
  const nextYear = m === 12 ? year + 1 : year;
  const nextMonth = m === 12 ? 1 : m + 1;
  return `${nextYear}-${String(nextMonth).padStart(2, "0")}-05`;
}

export function todayIso(now: Date = new Date()): string {
  const y = now.getFullYear();
  const m = String(now.getMonth() + 1).padStart(2, "0");
  const d = String(now.getDate()).padStart(2, "0");
  return `${y}-${m}-${d}`;
}

/**
 * Parses amounts typed the Brazilian way ("1.234,56", "R$ 600", "99,9") or with a dot ("600.50").
 * Returns null for anything that is not a number.
 */
export function parseAmount(input: string): number | null {
  let text = input.replace(/R\$|\s/g, "");
  if (text === "") return null;
  if (text.includes(",")) {
    text = text.replace(/\./g, "").replace(",", ".");
  } else if (/^\d{1,3}(\.\d{3})+$/.test(text)) {
    text = text.replace(/\./g, ""); // "1.500" means one thousand five hundred
  }
  if (!/^-?\d+(\.\d+)?$/.test(text)) return null;
  return Math.round(Number(text) * 100) / 100;
}

/** Amount as the user would type it back: 1234.5 → "1234,50" */
export function amountToInput(value: number): string {
  return value.toFixed(2).replace(".", ",");
}

export function statusLabel(line: Pick<ChargeLine, "status" | "isOverdue">): string {
  if (line.isOverdue) return line.status === "Partial" ? "Vencida (parcial)" : "Vencida";
  switch (line.status) {
    case "Paid":
      return "Paga";
    case "Partial":
      return "Parcial";
    default:
      return "Em aberto";
  }
}

/**
 * WhatsApp click-to-chat link. Brazilian numbers typed without the country code
 * ("(77) 99999-0000") get 55 prepended; numbers that already start with 55 are kept.
 * Returns null when there are not enough digits to be a phone number.
 */
export function whatsappLink(phone: string, message?: string): string | null {
  let digits = phone.replace(/\D/g, "").replace(/^0+/, "");
  if (digits.length === 10 || digits.length === 11) digits = `55${digits}`;
  if (digits.length < 12) return null;
  return message ? `https://wa.me/${digits}?text=${encodeURIComponent(message)}` : `https://wa.me/${digits}`;
}

/** "2027-01" + 5 → "2027-06" */
export function addMonths(month: string, count: number): string {
  const [year, m] = month.split("-").map(Number);
  const index = year * 12 + (m - 1) + count;
  return `${Math.floor(index / 12)}-${String((index % 12) + 1).padStart(2, "0")}`;
}

/** Months from one "yyyy-MM" to another, both inclusive ("2027-01" → "2027-06" = 6). */
export function monthsBetween(from: string, to: string): number {
  const [fy, fm] = from.split("-").map(Number);
  const [ty, tm] = to.split("-").map(Number);
  return (ty - fy) * 12 + (tm - fm) + 1;
}

/** Same rule as the API: equal parts rounded down to the cent, the last one takes the remainder. */
export function splitInstallments(total: number, parts: number): number[] {
  const cents = Math.round(total * 100);
  const base = Math.floor(cents / parts);
  return Array.from({ length: parts }, (_, i) => (i === parts - 1 ? cents - base * (parts - 1) : base) / 100);
}

/** Adds days to an ISO date ("2026-11-24" + 30 → "2026-12-24"), computed in UTC so DST never shifts the day. */
export function addDays(iso: string, days: number): string {
  const [y, m, d] = iso.split("-").map(Number);
  return new Date(Date.UTC(y, m - 1, d + days)).toISOString().slice(0, 10);
}

/** Same rule as the API: renewing while active keeps the remaining days; otherwise it counts from today. */
export function extendAccess(currentUntil: string | null, days: number, today: string): string {
  const start = currentUntil && currentUntil >= today ? currentUntil : today;
  return addDays(start, days);
}
