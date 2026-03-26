# PRD: RomCleanup v2.0 — Umfassendes ROM-Management

> **Status:** ❌ ARCHIVIERT (2026-03-11)
>
> Dieses PRD beschrieb die Anforderungen für den Ausbau der PowerShell-Version
> und die C#/.NET-Migration. Die Migration ist abgeschlossen.
>
> **Was umgesetzt wurde:**
> - C# .NET 10 Clean Architecture (Ports & Adapters) mit 7 Projekten
> - Regionsbasierte Deduplizierung (1G1R), Winner-Selection, GameKey-Normalisierung
> - DAT-Verifizierung (No-Intro, Redump, FBNEO) mit XXE-Schutz
> - Formatkonvertierung (CHD, RVZ, ZIP via chdman/dolphintool/7z)
> - Konsolen-Sortierung (100+ Konsolen aus `data/consoles.json`)
> - REST-API (ASP.NET Core Minimal API, API-Key-Auth, Rate-Limiting, SSE)
> - WPF-GUI (MVVM, Dark-Theme, Dashboard, Timeline)
> - CLI (headless, DryRun/Move, Exit-Codes)
> - Audit-CSV (SHA256-signiert), HTML-Reports, JSONL-Logs
> - 789+ xUnit-Tests
>
> **Nicht umgesetzt (bewusst auf Backlog):**
> - Plugin-System (C#-Neuimplementierung ausstehend)
> - Cloud-Sync, Docker-Container, Mobile-Web-UI
> - Community-Marktplatz, Telemetrie
>
> **Aktuelle Quellen:** `.github/copilot-instructions.md`, `docs/ARCHITECTURE_MAP.md`
