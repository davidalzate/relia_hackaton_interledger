# AI Usage Disclosure

## Tools Used

* Claude Code

## Where and How We Used AI

### 1. Backend – Payment flow (`/src/api/open-payments.ts`)

* Used Claude to scaffold the initial Open Payments flow, creating incoming and outgoing payments, as well as quote creation.

* Reviewed and rewrote error handling ourselves after testing revealed the AI's version didn't handle grants correctly.

### 2. Frontend – Agent and admin app (`/frontend/relia`)

* Used Claude Code to set up the Angular 21 project infrastructure (PrimeNG Aura theme, Zod, interceptors, route guard, lazy routes) and the team's visual design tokens.

* Used Claude Code to implement every screen from our written specs: login (A1), agent header and cash desk (B0–B1), deposit and withdrawal flows (B2–B3), cash confirmation and invite modals (B4–B5), load float (B6), cash on hand (B7), history (B8), receipt (B9), and the admin header and home (D0–D1).

* For each screen, Claude ran typecheck, build and unit tests and checked the flow in a browser before opening a pull request. Data is simulated in memory until the backend is connected.

### 3. Documentation – README setup instructions

* Used ChatGPT to draft the initial README structure.

* Edited by hand to match our actual setup steps and fix inaccurate install commands.

## What We Did Ourselves

* Testing and verification of all AI-suggested code

* Wrote the frontend screen specifications and visual conventions that Claude implemented

* Reviewed and merged every frontend pull request, and made the technical decisions (for example, staying on Angular 21 instead of downgrading to 19)

## Prompt Log

See `/prompts/backend-payments.md` for the full list of prompts used to generate the open payments flow code.

See `/docs/oscar_claude/frontend-prompts.md` for the full list of prompts used to build the frontend, with the screen specs and the pull request each one produced.