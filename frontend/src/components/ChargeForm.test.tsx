import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ChargeForm } from "./ChargeForm";

function setup() {
  const onSubmit = vi.fn().mockResolvedValue(undefined);
  render(<ChargeForm suggestions={[]} onSubmit={onSubmit} onCancel={() => {}} />);
  return { user: userEvent.setup(), onSubmit };
}

describe("ChargeForm plans", () => {
  it("splits a one-off service into installments", async () => {
    const { user, onSubmit } = setup();
    await user.type(screen.getByLabelText("Serviço"), "Regularização fiscal");
    await user.type(screen.getByLabelText("Valor (R$)"), "5000");
    await user.click(screen.getByLabelText("Parcelado"));
    await user.clear(screen.getByLabelText("Número de parcelas"));
    await user.type(screen.getByLabelText("Número de parcelas"), "5");

    expect(screen.getByText(/5 parcelas de R\$\s1\.000,00/)).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Gerar 5 cobranças" }));

    expect(onSubmit).toHaveBeenCalledWith(
      expect.objectContaining({ amount: 5000, kind: "OneOff", installments: 5, repeatUntil: null }),
    );
  });

  it("repeats a monthly fee until the chosen month", async () => {
    const { user, onSubmit } = setup();
    await user.type(screen.getByLabelText("Serviço"), "Honorários");
    await user.selectOptions(screen.getByLabelText("Tipo"), "Recurring");
    await user.clear(screen.getByLabelText("Referência"));
    await user.type(screen.getByLabelText("Referência"), "2027-01");
    await user.type(screen.getByLabelText("Valor (R$)"), "600");
    await user.click(screen.getByLabelText("Repetir todo mês"));
    await user.clear(screen.getByLabelText("Repetir até"));
    await user.type(screen.getByLabelText("Repetir até"), "2027-06");

    expect(screen.getByText(/6 mensalidades de jan\/2027 a jun\/2027/)).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Gerar 6 cobranças" }));

    expect(onSubmit).toHaveBeenCalledWith(
      expect.objectContaining({
        kind: "Recurring",
        reference: "2027-01-01",
        repeatUntil: "2027-06-01",
        installments: null,
      }),
    );
  });

  it("does not offer plans when editing an existing charge", () => {
    render(
      <ChargeForm
        initial={{
          id: "c1",
          clientId: "x",
          serviceName: "Honorários",
          description: null,
          reference: "2027-01-01",
          dueDate: "2027-02-05",
          amount: 600,
          kind: "Recurring",
        }}
        suggestions={[]}
        onSubmit={vi.fn()}
        onCancel={() => {}}
      />,
    );
    expect(screen.queryByLabelText("Repetir todo mês")).not.toBeInTheDocument();
  });
});
