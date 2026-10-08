import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import example from "../../../../business-details.example.json";
import { BusinessDetailsView } from "../view";

describe("business footer runtime data", () => {
  it.each([false, true])(
    "displays the supplied registration/contact details (compact=%s)",
    (compact) => {
      render(<BusinessDetailsView details={example} compact={compact} />);
      expect(screen.getByText(example.name)).toBeInTheDocument();
      expect(screen.getByText(new RegExp(`^ИНН: ${example.taxId}( ·|$)`))).toBeInTheDocument();
      expect(
        screen.getByText(new RegExp(`ОГРНИП: ${example.registrationId}$`)),
      ).toBeInTheDocument();
      for (const line of example.addressLines) {
        expect(screen.getByText(line)).toBeInTheDocument();
      }
      expect(screen.getByRole("link", { name: example.email })).toHaveAttribute(
        "href",
        `mailto:${example.email}`,
      );
    },
  );

  it("escapes operator-supplied text instead of treating it as markup", () => {
    const { container } = render(
      <BusinessDetailsView details={{ ...example, name: "<script>example</script>" }} />,
    );
    expect(screen.getByText("<script>example</script>")).toBeInTheDocument();
    expect(container.querySelector("script")).toBeNull();
  });
});
