import { describe, expect, it } from "vitest";

import { MAX_VOICE_ANSWER_LABEL } from "@/shared/config/trainer";
import { resolveVoiceTooLargeMessage } from "../voice-upload-error";

/** Axios распознаёт ошибку по флагу `isAxiosError === true` — для теста этого достаточно. */
function axiosError(status: number): unknown {
  return { isAxiosError: true, response: { status } };
}

describe("resolveVoiceTooLargeMessage (#663)", () => {
  it("returns an explicit message naming the duration limit for a raw HTTP 413 (nginx body cap)", () => {
    const message = resolveVoiceTooLargeMessage(axiosError(413));

    expect(message).not.toBeNull();
    expect(message).toContain("слишком большая");
    expect(message).toContain(MAX_VOICE_ANSWER_LABEL); // «до 3 мин»
  });

  it("returns null for other HTTP statuses (handled by the generic trainer error toast)", () => {
    expect(resolveVoiceTooLargeMessage(axiosError(400))).toBeNull();
    expect(resolveVoiceTooLargeMessage(axiosError(403))).toBeNull();
    expect(resolveVoiceTooLargeMessage(axiosError(500))).toBeNull();
  });

  it("returns null for a non-axios error (envelope / plain Error / unknown)", () => {
    expect(resolveVoiceTooLargeMessage(new Error("Request failed with status code 413"))).toBeNull();
    expect(resolveVoiceTooLargeMessage({ messages: [{ code: "trainer.transcribe.too_long" }] })).toBeNull();
    expect(resolveVoiceTooLargeMessage(null)).toBeNull();
    expect(resolveVoiceTooLargeMessage(undefined)).toBeNull();
  });
});
