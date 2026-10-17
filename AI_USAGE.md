# AI Usage Disclosure

## Tools Used

* Claude Code (VS Code extension)
* Productivity skills for Claude Code: https://github.com/mattpocock/skills (`wayfinder`, `grilling`, `to-spec`, `research`, `ask-matt`)
* ChatGPT (GPT)

## Where and How We Used AI

### 1. Architecture and API contracts (`/contracts`, `/docs/adr`, `/docs/wayfinder`)

* Used Claude Code with the `wayfinder` and `grilling` skills to turn our business model (`docs/business_model.txt`) and prototype (`docs/prototype.md`) into the architecture we had chosen: Angular frontend → .NET 10 backend (business logic) → Node.js rail proxy (Open Payments).
* Claude drafted the OpenAPI contracts (`relia-api.openapi.yaml`, `rail-proxy.openapi.yaml`), the Postman collections, and the usage guide in `contracts/README.md`, across several grilling sessions where we questioned and changed each decision.
* In the same session Claude wrote ADR 0001 (the proxy as the rail contract) and the domain glossary (`GLOSSARY.md`), based on the answers we gave during grilling.

### 2. Rail proxy – Open Payments integration (`/rail-proxy`)

* Used the `to-spec` skill to write the proxy spec and break it into 9 implementation tickets (`docs/issues/proxy-de-riel-node/`).
* Claude Code implemented the tickets one by one with the Open Payments Node SDK: wallet address resolution, Relia's permanent GNAP grant, outgoing payments (quote + outgoing), incoming payments, idempotency for `POST /payments`, grant token rotation and spend limits, and payment expiry and cancellation.
* Used the `research` skill to check how the proxy uses the SDK against the official Open Payments documentation (`docs/research/open-payments-sdk.md`) and to write that section of the proxy README.
* We ran payments against the Interledger test wallet (`wallet.interledger-test.dev`) while building and reported what failed. For example, a real payment returned 403; Claude traced it to a grant interval that had not started yet, and the proxy now rejects those grants and explains the error. The full end-to-end rehearsal (ticket 09) is still pending.

### 3. Backend – Relia API (`/backend`)

* TODO: describe how AI was used for the backend spec (`docs/wayfinder/relia-api`) and the correspondent endpoints (in progress).

### 4. Frontend – Agent and admin app (`/frontend/relia`)

* Used Claude Code to set up the Angular 21 project (PrimeNG Aura theme, Zod, interceptors, route guard, lazy routes), the team's visual design tokens, and the HTTP layer for the Relia API (auth, errors, idempotency, polling).
* Used Claude Code to implement every screen from our written specs: login (A1), agent header and cash desk (B0–B1), deposit and withdrawal flows (B2–B3), cash confirmation and invite modals (B4–B5), load float (B6), cash on hand (B7), history (B8), receipt (B9), and the admin header and home (D0–D1).
* For each screen, Claude ran typecheck, build and unit tests, and checked the flow in a browser before opening a pull request. Data is simulated in memory until the backend is connected.

### 5. Documentation

* Claude wrote `contracts/README.md` (how to use the Postman collections), the endpoint flow guide (`contracts/FLUJOS.md`) and the Open Payments SDK section of the rail proxy README.

## What We Did Ourselves

* Defined the problem to be solved with Open Payments and ILP, the business model (`docs/business_model.txt`), the prototype (`docs/prototype.md`), the data schema (`docs/relia_schema_hackathon_v0.2.sql`) and the screen specifications (`docs/detalle_pantallas.md`). These were the inputs we gave to Claude
* Defined the project architecture and technology stack. AI proposed options within it; we made the decisions (for example, isolating Open Payments in a Node proxy, and staying on Angular 21 instead of downgrading to 19)
* Tested the AI-generated code, including real payments on the Interledger test network
* Reviewed and merged the frontend pull requests

## Prompt Log

* `/docs/jeff_claude/`: full Claude Code sessions, one file per session:
  * `wayfinder`: architecture (Angular → .NET 10 → Node proxy) and the OpenAPI contracts for the backend and the proxy, from the business model, prototype and screen specs.
  * `ask-matt`: the guide for using the Postman collections built from the contracts (`contracts/README.md`).
  * `to-spec` (proxy): spec and tickets for the Node rail proxy with the Open Payments SDK.
  * `research`: how the proxy uses the Open Payments SDK, and the SDK section of the proxy README.
  * `to-spec` (backend): spec and tickets for the .NET backend (`docs/issues/backend-relia-api/`), including the local PostgreSQL database in Docker from `backend/db/db_ddl.sql`.
  * `implement` (two sessions): backend tickets 05 (wallet keys, WhatsApp invitation and simulated inbox) and 12 (admin network panel, `GET /agents`).
* `/docs/oscar_claude/frontend-prompts.md`: prompts used to build the frontend, with the screen specs and the pull request each one produced.
* `/docs/johny_claude/2026-10-16-prompts-backend-relia-api.md`: key prompts for the .NET backend (Relia API): project setup (.NET 10, Swagger, `.gitignore`), spec and tickets, controller/service/repository skeleton, EF Core with PostgreSQL, ticket implementation and the backend README.
