import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { api } from "../api";
import { SignupPage } from "./SignupPage";

function renderPage() {
  render(
    <MemoryRouter>
      <SignupPage />
    </MemoryRouter>,
  );
  return userEvent.setup();
}

async function fill(user: ReturnType<typeof userEvent.setup>, confirm: string) {
  await user.type(screen.getByLabelText("Nome ou nome do negócio"), "Salão Bella");
  await user.type(screen.getByLabelText("E-mail"), "salao@exemplo.com");
  await user.type(screen.getByLabelText("WhatsApp com DDD"), "(77) 99999-0000");
  await user.type(screen.getByLabelText("Senha"), "senha-forte-1");
  await user.type(screen.getByLabelText("Confirme a senha"), confirm);
}

describe("SignupPage", () => {
  afterEach(() => vi.restoreAllMocks());

  it("does not send the request when the passwords differ", async () => {
    const signup = vi.spyOn(api, "signup");
    const user = renderPage();

    await fill(user, "outra-senha");
    await user.click(screen.getByRole("button", { name: "Enviar solicitação" }));

    expect(signup).not.toHaveBeenCalled();
    expect(screen.getByText("As senhas não conferem.")).toBeInTheDocument();
  });

  it("sends the request with an empty honeypot and shows the confirmation", async () => {
    const signup = vi.spyOn(api, "signup").mockResolvedValue({ message: "Solicitação recebida." });
    const user = renderPage();

    await fill(user, "senha-forte-1");
    await user.click(screen.getByRole("button", { name: "Enviar solicitação" }));

    expect(signup).toHaveBeenCalledWith(expect.objectContaining({ email: "salao@exemplo.com", website: "" }));
    expect(await screen.findByText("Solicitação enviada")).toBeInTheDocument();
  });
});
