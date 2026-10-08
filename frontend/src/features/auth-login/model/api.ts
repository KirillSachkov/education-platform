import { AUTH_ORIGIN } from "@/shared/config";
import { EnvelopeError, type ApiError, type ErrorMessage } from "@/shared/api";

export type SendOtpRequest = {
  email: string;
};

export type VerifyOtpRequest = {
  email: string;
  code: string;
  consentOfferAccepted?: boolean;
  consentPersonalDataAccepted?: boolean;
  consentMarketingAccepted?: boolean;
};

export const authLoginApi = {
  sendOtp: async (request: SendOtpRequest) => {
    const res = await fetch(`${AUTH_ORIGIN}/auth/otp/send`, {
      method: "POST",
      credentials: "include",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(request),
    });

    if (!res.ok) {
      const data = (await res.json().catch(() => null)) as {
        messages?: ErrorMessage[];
        error?: string | ApiError;
      } | null;

      // Envelope-формат бэкенда: { error: { messages: [{ code, message }], type } }.
      // Раньше тут был `new Error(data?.error)` — при объекте error это давало
      // String(obj) === "[object Object]" в тосте. Теперь кидаем EnvelopeError,
      // чтобы сообщение прошло через тот же i18n/translate-слой, что и остальной API.
      if (data?.error && typeof data.error === "object" && Array.isArray(data.error.messages)) {
        throw new EnvelopeError(data.error);
      }

      const legacy =
        (typeof data?.error === "string" ? data.error : undefined) ?? data?.messages?.[0]?.message;
      throw new Error(legacy ?? "Не удалось отправить код");
    }
  },

  verifyOtp: async (request: VerifyOtpRequest) => {
    const res = await fetch(`${AUTH_ORIGIN}/auth/otp/verify`, {
      method: "POST",
      credentials: "include",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(request),
    });

    if (!res.ok) {
      const data = (await res.json().catch(() => null)) as {
        messages?: Array<{ code: string; message: string }>;
        error?: string | { messages?: Array<{ code: string; message: string }> };
      } | null;

      // Envelope-формат бэкенда: { error: { messages: [{ code, message }] } }
      // плюс legacy plain-string form. Поддерживаем оба.
      const envelopeMessages = typeof data?.error === "object" ? data.error.messages : undefined;
      const messages = envelopeMessages ?? data?.messages;
      const errorString = typeof data?.error === "string" ? data.error : undefined;

      const message = messages?.[0]?.message ?? errorString ?? "Неверный код";
      const code = messages?.[0]?.code;
      throw new VerifyOtpError(message, code);
    }
  },
};

export class VerifyOtpError extends Error {
  constructor(
    message: string,
    public readonly code?: string,
  ) {
    super(message);
    this.name = "VerifyOtpError";
  }
}

export const CONSENT_MISSING_ERROR_CODE = "auth.consent.mandatory.missing";
