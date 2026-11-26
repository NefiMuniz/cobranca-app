import { StrictMode, type ReactNode } from "react";
import { createRoot } from "react-dom/client";
import { BrowserRouter, Navigate, Route, Routes } from "react-router-dom";
import { AuthProvider, useAuth } from "./auth";
import { ClientFormPage } from "./pages/ClientFormPage";
import { ClientPage } from "./pages/ClientPage";
import { DashboardPage } from "./pages/DashboardPage";
import { AdminAccountPage } from "./pages/AdminAccountPage";
import { AdminAccountsPage } from "./pages/AdminAccountsPage";
import { LoginPage } from "./pages/LoginPage";
import { SignupPage } from "./pages/SignupPage";
import "./styles.css";

function RequireAuth({ children }: { children: ReactNode }) {
  const { isAuthenticated } = useAuth();
  return isAuthenticated ? children : <Navigate to="/login" replace />;
}

/** UI guard only; the API enforces the admin role on every /api/admin call. */
function RequireAdmin({ children }: { children: ReactNode }) {
  const { isAuthenticated, role } = useAuth();
  if (!isAuthenticated) return <Navigate to="/login" replace />;
  return role === "admin" ? children : <Navigate to="/" replace />;
}

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <AuthProvider>
      <BrowserRouter>
        <Routes>
          <Route path="/login" element={<LoginPage />} />
          <Route path="/solicitar-acesso" element={<SignupPage />} />
          <Route path="/admin" element={<RequireAdmin><AdminAccountsPage /></RequireAdmin>} />
          <Route path="/admin/contas/:id" element={<RequireAdmin><AdminAccountPage /></RequireAdmin>} />
          <Route path="/" element={<RequireAuth><DashboardPage /></RequireAuth>} />
          <Route path="/clientes/novo" element={<RequireAuth><ClientFormPage /></RequireAuth>} />
          <Route path="/clientes/:id" element={<RequireAuth><ClientPage /></RequireAuth>} />
          <Route path="/clientes/:id/editar" element={<RequireAuth><ClientFormPage /></RequireAuth>} />
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </BrowserRouter>
    </AuthProvider>
  </StrictMode>,
);
