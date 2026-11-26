/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_API_BASE_URL?: string;
  /** Operator's WhatsApp, shown to subscribers whose access is about to expire. */
  readonly VITE_SUPPORT_WHATSAPP?: string;
}
