import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ChargeLine } from "../types";
import { PaymentForm } from "./PaymentForm";

const openLine: ChargeLine = {
  charge: {
    id: "c1",
    clientId: "x",
    serviceName: "Honorários",
    description: null,
    reference: "2026-10-01",
    dueDate: "2026-11-05",
    amount: 600,
    kind: "Recurring",
  },
  paid: 200,
  remaining: 400,
  status: "Partial",
  isOverdue: true,
};

describe("PaymentForm", () => {
  it("prefills the remaining amount when a charge is chosen and submits it", async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn().mockResolvedValue(undefined);
    render(<PaymentForm openLines={[openLine]} onSubmit={onSubmit} onCancel={() => {}} />);

    await user.selectOptions(screen.getByLabelText("Abater de"), "c1");
    expect(screen.getByLabelText("Valor (R$)")).toHaveValue("400,00");

    await user.click(screen.getByRole("button", { name: "Registrar pagamento" }));

    expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({ amount: 400, chargeId: "c1" }));
  });

  it("blocks an amount that is not a number", async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn();
    render(<PaymentForm openLines={[]} onSubmit={onSubmit} onCancel={() => {}} />);

    await user.type(screen.getByLabelText("Valor (R$)"), "abc");
    await user.click(screen.getByRole("button", { name: "Registrar pagamento" }));

    expect(onSubmit).not.toHaveBeenCalled();
    expect(screen.getByText(/Valor inválido/)).toBeInTheDocument();
  });
});
