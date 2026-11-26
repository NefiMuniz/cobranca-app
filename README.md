# Cobranças

**A mobile-first billing tracker for service providers: what each client owes, what is overdue, and what came in this month.**
**Live demo: https://d162k6qcmhtiu6.cloudfront.net/login**
Built with C# / ASP.NET Core and React, deployed serverless on AWS.

---

## The problem

Freelancers and small accounting practices often track billing in spreadsheets: one block of columns per client, manual `=SUM()` formulas, and dates typed by hand. Three things go wrong all the time:

- **Partial payments are hard to reconcile.** A client pays R$ 500 against R$ 1,650 spread over three services, and the open balance has to be worked out by hand.
- **Overdue items are invisible** until someone scrolls through every block of columns.
- **Recurring fees and installment plans** have to be typed again every month.

Cobranças replaces that spreadsheet with an app that works on a phone, between client visits.

## What it does

| | |
|---|---|
| **Dashboard** | Total receivable, overdue amount and number of overdue charges, amount received this month. Clients are sorted by urgency. |
| **Flexible charges** | Each charge has a service name and a free-text description, not a rigid price catalog. Charges are either one-off services or monthly fees. |
| **Monthly fees** | Bill January and repeat it through June in one step. Each month gets its own charge and due date. |
| **Installments** | A R$ 5,000 service in 5 installments becomes 5 monthly charges of R$ 1,000. Amounts are split to the cent and always add up to the total. |
| **Payment allocation** | A payment can settle a specific charge. If it is not linked to one, it settles the oldest open charges first. Overpayments become client credit. |
| **Status per charge** | Open, partially paid, paid or overdue. "Today" is computed in the business time zone, not the server's. |
| **One tap to WhatsApp** | The client's phone number opens a WhatsApp chat to send a payment reminder. |
| **Multi-tenant SaaS** | Each subscriber has an isolated account. New users request access and the operator approves them. |
| **Paid access periods** | The operator grants N days after a payment; renewals keep the days still left. Expired or suspended accounts are blocked on their next request, and users see a warning before expiry. |

## Tech stack

| Layer | Technology |
|---|---|
| Backend | C# 14, ASP.NET Core 10 minimal APIs, JWT authentication, built-in rate limiting |
| Domain | Plain C# class library with no framework dependencies (billing rules, installment planning) |
| Frontend | React 19, TypeScript (strict), Vite, React Router; mobile-first CSS with no UI framework |
| Data | Amazon DynamoDB, single-table design |
| Hosting | AWS Lambda (Function URL) for the API, S3 + CloudFront for the SPA, defined in AWS SAM |
| Quality | xUnit unit and integration tests (`WebApplicationFactory`), Vitest + Testing Library, GitHub Actions CI |

## Architecture

```
 Phone / browser
       │
       ▼
 CloudFront ──► S3 (React SPA)
       │
       │ HTTPS + JWT
       ▼
 Lambda Function URL ──► ASP.NET Core API ──► DynamoDB
                          ├─ Auth (PBKDF2 + JWT with tenant and role claims)
                          ├─ Rate limiting (per account / per IP)
                          ├─ Access check on every request (expiry, suspension)
                          └─ Domain: BalanceCalculator, ChargePlanner, AccessPolicy
```

### Engineering decisions

- **The business rules are isolated and tested on their own.** Payment allocation, overdue detection and installment splitting live in a dependency-free domain project. They are covered by fast unit tests, including a case that reproduces the original spreadsheet's numbers.
- **One codebase runs locally and on Lambda.** The same ASP.NET Core app runs on Kestrel during development and inside Lambda in production, with no Lambda-specific handler code.
- **Storage can be swapped.** A repository interface has an in-memory implementation (local development and integration tests) and a DynamoDB implementation (production).
- **Tenant isolation is enforced by construction.** The tenant id comes only from the signed token, never from the URL or the request body. Every repository method requires it, and it forms the DynamoDB partition key. There is no code path that reads across tenants. Integration tests try to access another tenant's records using their real ids, and every attempt returns 404.
- **The DynamoDB model is built around the main query.** Each tenant is one partition. The dashboard is a single query; a client's ledger is the same query with `begins_with` on the sort key. There are no table scans. Accounts and a unique e-mail guard are written together in one conditional transaction.
- **The system is designed to cost close to nothing.** Lambda and DynamoDB (provisioned capacity) stay within AWS always-free limits. A Function URL replaces API Gateway, so there is one less billed service.
- **Security fails closed.** The API refuses logins until its secrets are configured. Passwords are hashed with PBKDF2-SHA256 and compared in constant time. Unknown e-mails take as long to reject as wrong passwords, and sign-up responses never reveal whether an e-mail is already registered. No credentials are stored in the repository.
- **Abuse is cheap to reject, and spend is capped.**
  - Anonymous traffic is rate-limited per IP.
  - Login and sign-up have stricter limits, a honeypot field and a cap on pending requests.
  - Requests are rejected before any database read or password hashing.
  - DynamoDB runs on provisioned capacity, so a flood is throttled instead of billed.
  - The Lambda timeout is short, logs have a retention period and log only warnings, and there is an optional concurrency cap and a budget alert.

## Quality

- **Backend:** domain tests (allocation, overdue rules, installment rounding, month boundaries), password hashing tests, and API integration tests that run the real HTTP pipeline: authentication, validation and JSON contracts.
- **Frontend:** tests for parsing and formatting Brazilian currency, the installment and repetition forms, and payment prefill.
- **CI:** every push runs the backend build and tests, the TypeScript type check, the frontend tests and the production build.

## Repository layout

```
backend/
  src/Cobranca.Domain/     billing rules (no dependencies)
  src/Cobranca.Api/        HTTP API, authentication, validation, storage adapters
  tests/Cobranca.Tests/    unit and integration tests
frontend/                  React + TypeScript SPA
infra/                     AWS SAM template and deploy script
.github/workflows/         CI pipeline
```

## Roadmap

- Automatic WhatsApp or e-mail reminders for overdue charges
- Import from the legacy spreadsheet layout (.xlsx)
- Edit or cancel a whole series of monthly fees or installments at once
- Online payments (Pix) that extend access automatically
- Higher plans (limits and features are already centralized in `PlanCatalog`)

---

Built by **Néfi Muniz**: [LinkedIn](https://www.linkedin.com/in/nefimuniz/) · [GitHub](https://github.com/NefiMuniz)
