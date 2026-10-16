# AI Usage Disclosure

## Tools Used

* Claude Code

## Where and How We Used AI

### 1. Backend – Payment flow (`/src/api/open-payments.ts`)

* Used Claude to scaffold the initial Open Payments flow, creating incoming and outgoing payments, as well as quote creation.

* Reviewed and rewrote error handling ourselves after testing revealed the AI's version didn't handle grants correctly.

### 2. Frontend – Donation platform UI (`/src/components/Donation.tsx`)

* Used Claude to generate all design and frontend pages and components

### 3. Documentation – README setup instructions

* Used ChatGPT to draft the initial README structure.

* Edited by hand to match our actual setup steps and fix inaccurate install commands.

## What We Did Ourselves

* Testing and verification of all AI-suggested code

## Prompt Log

See `/prompts/backend-payments.md` for the full list of prompts used to generate the open payments flow code.