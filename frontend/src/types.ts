// Mirrors the JSON contracts of the ASP.NET Core API (camelCase, decimals as numbers, dates as yyyy-MM-dd).

export type ChargeKind = "OneOff" | "Recurring";
export type ChargeStatus = "Open" | "Partial" | "Paid";

export interface Client {
  id: string;
  name: string;
  phone: string | null;
  notes: string | null;
  createdAt: string;
}

export interface Charge {
  id: string;
  clientId: string;
  serviceName: string;
  description: string | null;
  reference: string;
  dueDate: string;
  amount: number;
  kind: ChargeKind;
}

export interface Payment {
  id: string;
  clientId: string;
  amount: number;
  paidOn: string;
  chargeId: string | null;
  note: string | null;
}

export interface ChargeLine {
  charge: Charge;
  paid: number;
  remaining: number;
  status: ChargeStatus;
  isOverdue: boolean;
}

export interface ClientStatement {
  totalCharged: number;
  totalPaid: number;
  outstanding: number;
  overdueAmount: number;
  credit: number;
  openCharges: number;
  overdueCharges: number;
  lines: ChargeLine[];
}

export interface ClientDetail {
  client: Client;
  statement: ClientStatement;
  payments: Payment[];
}

export interface ClientSummary {
  id: string;
  name: string;
  phone: string | null;
  outstanding: number;
  overdueAmount: number;
  credit: number;
  openCharges: number;
  overdueCharges: number;
  nextDueDate: string | null;
}

export interface Dashboard {
  today: string;
  totalOutstanding: number;
  totalOverdue: number;
  overdueCharges: number;
  receivedThisMonth: number;
  clients: ClientSummary[];
}

export interface ClientInput {
  name: string;
  phone?: string;
  notes?: string;
}

export interface ChargeInput {
  serviceName: string;
  description?: string;
  reference: string | null;
  dueDate: string | null;
  amount: number | null;
  kind: ChargeKind;
  /** Creation only: generate one monthly fee per month until this month (yyyy-MM-01). */
  repeatUntil?: string | null;
  /** Creation only: split the amount into this many monthly installments. */
  installments?: number | null;
}

export interface PaymentInput {
  amount: number | null;
  paidOn: string | null;
  chargeId?: string | null;
  note?: string;
}

export type Role = "admin" | "user";
export type AccountStatus = "Pending" | "Active" | "Suspended" | "Rejected";
export type AccessState = "Pending" | "Active" | "Expired" | "Suspended" | "Rejected";

export interface LoginResponse {
  token: string;
  expiresAt: string;
  role: Role;
  name: string;
}

export interface Me {
  role: Role;
  name: string;
  email: string | null;
  plan: string;
  state: AccessState;
  accessUntil: string | null;
  daysLeft: number | null;
}

export interface SignupInput {
  name: string;
  email: string;
  phone: string;
  password: string;
  /** Honeypot: hidden from people, filled only by bots. */
  website: string;
}

export interface AccountView {
  id: string;
  name: string;
  email: string;
  phone: string | null;
  status: AccountStatus;
  state: AccessState;
  plan: string;
  createdAt: string;
  accessUntil: string | null;
  daysLeft: number | null;
  adminNote: string | null;
}

export interface AccessGrant {
  id: string;
  accountId: string;
  days: number;
  amountPaid: number | null;
  note: string | null;
  grantedAt: string;
  previousUntil: string | null;
  newUntil: string;
}

export interface AccountDetail {
  account: AccountView;
  grants: AccessGrant[];
}

export interface GrantInput {
  days: number | null;
  amountPaid: number | null;
  note?: string;
}
