import { render } from "@testing-library/react";
import { expect, it, vi } from "vitest";
import { AdminCampaignsPage } from "../admin-campaigns-page";

const hooks = vi.hoisted(() => ({
  count: vi.fn((_slug: string) => ({ data: { count: 1 }, isLoading: false })),
  test: vi.fn(() => ({ mutate: vi.fn(), isPending: false })),
  run: vi.fn((_slug: string) => ({ mutateAsync: vi.fn(), isPending: false })),
}));
vi.mock("../../model/use-recipient-count", () => ({ useRecipientCount: hooks.count }));
vi.mock("../../model/use-send-test-campaign", () => ({ useSendTestCampaign: hooks.test }));
vi.mock("../../model/use-run-campaign", () => ({ useRunCampaign: hooks.run }));

it("mounts only account campaigns without querying the retired level test audience", () => {
  render(<AdminCampaignsPage />);

  expect(hooks.count.mock.calls.map(([slug]) => slug)).toEqual([
    "email-login-notice",
    "link-accounts-nudge",
  ]);
  expect(hooks.run.mock.calls.map(([slug]) => slug)).toEqual([
    "email-login-notice",
    "link-accounts-nudge",
  ]);
});
