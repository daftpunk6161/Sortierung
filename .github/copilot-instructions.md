# Romulus – Copilot Instructions

Die verbindlichen Regeln liegen in [AGENTS.md](../AGENTS.md) (Single Source of Truth).

Modulare Tiefenregeln (architecture, cleanup, conversion, gui, release, review, testing) liegen in [.github/instructions/](./instructions/) und greifen automatisch via `applyTo: "**"`.

## Copilot-spezifisch

- Prompt-Sammlungen (zum Copy-Paste, keine Slash-Prompts): [.github/prompts/](./prompts/)
- PR-Template: [.github/PULL_REQUEST_TEMPLATE.md](./PULL_REQUEST_TEMPLATE.md)
- Bei Konflikt zwischen Quellen gewinnt immer AGENTS.md.

## Hinweis zur Vermeidung doppelter Wahrheit

Diese Datei darf keine eigenen Regeln definieren. Sie verweist ausschliesslich auf AGENTS.md und die modularen Instructions. Aenderungen an Regeln gehoeren in AGENTS.md.
