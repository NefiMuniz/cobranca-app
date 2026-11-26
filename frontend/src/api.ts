import type {
  AccountDetail,
  AccountView,
  Charge,
  ChargeInput,
  Client,
  ClientDetail,
  ClientInput,
  ClientSummary,
  Dashboard,
  GrantInput,
  LoginResponse,
  Me,
  Payment,
  PaymentInput,
  Role,
  SignupInput,
} from "./types";

const BASE_URL = (import.meta.env.VITE_API_BASE_URL ?? "").replace(/\/$/, "");
const TOKEN_KEY = "cobranca.session";

export interface Session {
  token: string;
  expiresAt: string;
  role: Role;
  name: string;
}

/** Error carrying the API's ProblemDetails: a title plus optional per-field messages. */
export class ApiError extends Error {
  constructor(
    message: string,
    public readonly status: number,
    public readonly fieldErrors: Record<string, string[]> = {},
    /** Machine-readable reason sent by the API (e.g. "expired", "suspended", "pending"). */
    public readonly code?: string,
  ) {
    super(message);
    this.name = "ApiError";
  }
}

// Browser storage can be unavailable (private mode, sandboxed frames), so every access is guarded.
export const sessionStore = {
  read(): Session | null {
    try {
      const raw = localStorage.getItem(TOKEN_KEY);
      if (!raw) return null;
      const session = JSON.parse(raw) as Session;
      if (!session.role) return null; // stored by an older version
      return new Date(session.expiresAt).getTime() > Date.now() ? session : null;
    } catch {
      return null;
    }
  },
  write(session: Session | null): void {
    try {
      if (session) localStorage.setItem(TOKEN_KEY, JSON.stringify(session));
      else localStorage.removeItem(TOKEN_KEY);
    } catch {
      // Session then lives only in memory for this tab.
    }
  },
};

let currentToken: string | null = sessionStore.read()?.token ?? null;
let onUnauthorized: () => void = () => {};
let onAccessBlocked: (message: string) => void = () => {};

/** Codes for accounts that are signed in but may not use the app right now. */
const BLOCKED_CODES = new Set(["expired", "suspended", "pending", "rejected"]);

export function setToken(token: string | null): void {
  currentToken = token;
}

export function setUnauthorizedHandler(handler: () => void): void {
  onUnauthorized = handler;
}

export function setAccessBlockedHandler(handler: (message: string) => void): void {
  onAccessBlocked = handler;
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers);
  if (init.body) headers.set("Content-Type", "application/json");
  if (currentToken) headers.set("Authorization", `Bearer ${currentToken}`);

  let response: Response;
  try {
    response = await fetch(`${BASE_URL}${path}`, { ...init, headers });
  } catch {
    throw new ApiError("Sem conexão com o servidor. Tente novamente.", 0);
  }

  if (response.status === 401 && path !== "/api/auth/login") {
    onUnauthorized();
  }

  if (!response.ok) {
    let title = "Não foi possível concluir a operação.";
    let errors: Record<string, string[]> = {};
    let code: string | undefined;
    try {
      const problem = (await response.json()) as { title?: string; errors?: Record<string, string[]>; code?: string };
      code = problem.code;
      if (problem.errors) {
        errors = problem.errors;
        title = "Revise os campos destacados.";
      } else if (problem.title) {
        title = problem.title;
      }
    } catch {
      // Body was not JSON; keep the generic message.
    }
    if (response.status === 429) title = "Muitas tentativas. Aguarde um minuto.";
    if (response.status === 403 && code && BLOCKED_CODES.has(code) && path !== "/api/auth/login") {
      onAccessBlocked(title);
    }
    throw new ApiError(title, response.status, errors, code);
  }

  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}

const json = (body: unknown) => JSON.stringify(body);

export const api = {
  login: (username: string, password: string) =>
    request<LoginResponse>("/api/auth/login", {
      method: "POST",
      body: json({ username, password }),
    }),
  signup: (input: SignupInput) => request<{ message: string }>("/api/signup", { method: "POST", body: json(input) }),
  me: () => request<Me>("/api/me"),

  adminAccounts: () => request<AccountView[]>("/api/admin/accounts"),
  adminAccount: (id: string) => request<AccountDetail>(`/api/admin/accounts/${encodeURIComponent(id)}`),
  adminGrant: (id: string, input: GrantInput) =>
    request<AccountDetail>(`/api/admin/accounts/${encodeURIComponent(id)}/grant`, { method: "POST", body: json(input) }),
  adminAction: (id: string, action: "suspend" | "reactivate" | "reject", note?: string) =>
    request<AccountDetail>(`/api/admin/accounts/${encodeURIComponent(id)}/${action}`, {
      method: "POST",
      body: json({ note: note ?? null }),
    }),

  dashboard: () => request<Dashboard>("/api/dashboard"),
  listClients: () => request<ClientSummary[]>("/api/clients"),
  getClient: (id: string) => request<ClientDetail>(`/api/clients/${encodeURIComponent(id)}`),
  createClient: (input: ClientInput) => request<Client>("/api/clients", { method: "POST", body: json(input) }),
  updateClient: (id: string, input: ClientInput) =>
    request<Client>(`/api/clients/${encodeURIComponent(id)}`, { method: "PUT", body: json(input) }),
  deleteClient: (id: string) => request<void>(`/api/clients/${encodeURIComponent(id)}`, { method: "DELETE" }),

  createCharge: (clientId: string, input: ChargeInput) =>
    // Returns every charge created: one, or several for repetitions and installments.
    request<Charge[]>(`/api/clients/${encodeURIComponent(clientId)}/charges`, { method: "POST", body: json(input) }),
  updateCharge: (clientId: string, chargeId: string, input: ChargeInput) =>
    request<Charge>(`/api/clients/${encodeURIComponent(clientId)}/charges/${encodeURIComponent(chargeId)}`, {
      method: "PUT",
      body: json(input),
    }),
  deleteCharge: (clientId: string, chargeId: string) =>
    request<void>(`/api/clients/${encodeURIComponent(clientId)}/charges/${encodeURIComponent(chargeId)}`, {
      method: "DELETE",
    }),

  createPayment: (clientId: string, input: PaymentInput) =>
    request<Payment>(`/api/clients/${encodeURIComponent(clientId)}/payments`, { method: "POST", body: json(input) }),
  deletePayment: (clientId: string, paymentId: string) =>
    request<void>(`/api/clients/${encodeURIComponent(clientId)}/payments/${encodeURIComponent(paymentId)}`, {
      method: "DELETE",
    }),
};
