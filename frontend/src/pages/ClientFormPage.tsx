import { useEffect, useState, type FormEvent } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { api, ApiError } from "../api";
import { AppHeader, ErrorBanner, Field, Loading } from "../components/ui";

export function ClientFormPage() {
  const { id } = useParams();
  const navigate = useNavigate();
  const editing = Boolean(id);
  const [name, setName] = useState("");
  const [phone, setPhone] = useState("");
  const [notes, setNotes] = useState("");
  const [loaded, setLoaded] = useState(!editing);
  const [errors, setErrors] = useState<Record<string, string[]>>({});
  const [message, setMessage] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    if (!id) return;
    api
      .getClient(id)
      .then(({ client }) => {
        setName(client.name);
        setPhone(client.phone ?? "");
        setNotes(client.notes ?? "");
        setLoaded(true);
      })
      .catch(() => setMessage("Cliente não encontrado."));
  }, [id]);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setSaving(true);
    setMessage(null);
    try {
      const input = { name, phone, notes };
      const client = id ? await api.updateClient(id, input) : await api.createClient(input);
      navigate(`/clientes/${client.id}`, { replace: true });
    } catch (e) {
      if (e instanceof ApiError) {
        setErrors(e.fieldErrors);
        setMessage(e.message);
      } else {
        setMessage("Erro inesperado.");
      }
    } finally {
      setSaving(false);
    }
  };

  const remove = async () => {
    if (!id) return;
    if (!window.confirm(`Excluir "${name}" com todas as cobranças e pagamentos? Esta ação não pode ser desfeita.`)) return;
    try {
      await api.deleteClient(id);
      navigate("/", { replace: true });
    } catch (e) {
      setMessage(e instanceof ApiError ? e.message : "Erro inesperado.");
    }
  };

  return (
    <>
      <AppHeader title={editing ? "Editar cliente" : "Novo cliente"} back={id ? `/clientes/${id}` : "/"} />
      <main className="page">
        {!loaded && !message && <Loading />}
        {loaded && (
          <form className="form" onSubmit={submit} noValidate>
            <ErrorBanner message={message} />
            <Field label="Nome" error={errors.name}>
              {(fieldId, describedBy) => (
                <input id={fieldId} aria-describedby={describedBy} value={name} onChange={(e) => setName(e.target.value)} required />
              )}
            </Field>
            <Field label="Telefone (opcional)" error={errors.phone}>
              {(fieldId, describedBy) => (
                <input
                  id={fieldId}
                  type="tel"
                  inputMode="tel"
                  aria-describedby={describedBy}
                  value={phone}
                  onChange={(e) => setPhone(e.target.value)}
                />
              )}
            </Field>
            <Field label="Observações (opcional)" error={errors.notes} hint="Ex.: dia de vencimento combinado, forma de pagamento.">
              {(fieldId, describedBy) => (
                <textarea id={fieldId} aria-describedby={describedBy} rows={3} value={notes} onChange={(e) => setNotes(e.target.value)} />
              )}
            </Field>
            <div className="form-actions">
              <button type="submit" className="button button-primary" disabled={saving}>
                {saving ? "Salvando…" : "Salvar"}
              </button>
            </div>
            {editing && (
              <button type="button" className="button button-danger-ghost" onClick={remove}>
                Excluir cliente
              </button>
            )}
          </form>
        )}
        {!loaded && message && <ErrorBanner message={message} />}
      </main>
    </>
  );
}
