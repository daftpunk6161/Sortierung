# Fix Diagnostics

## Purpose

Resolve issues reported by the Chat Customizations Evaluations analyzer in prompt, agent, skill, and instruction files. Diagnostic classes include: contradictions, ambiguities, persona conflicts, cognitive load (constraint-overload, deep-decision-tree, priority-conflict), and coverage gaps.

## Usage

Invoked when the user clicks the "Fix Diagnostics" button on an open customization file. The skill receives the current diagnostics list (line, code, message, optional suggestion) and applies targeted edits to the file.

## Operating Principles

### 1. Intent over surface

- Preserve the **intent** of every existing instruction unless explicitly required by a diagnostic to modify or remove the instruction.
- Preserve structure and tone by default. Both may be adjusted only when a diagnostic explicitly demands it (e.g., a contradiction that requires removing a clause, or a coverage-gap that requires expanding one).
- Never silently change the meaning of a rule. If a suggestion would shift the meaning, reject it and document why.

### 2. Source language

- Edits MUST stay in the source language of the file (DE stays DE, EN stays EN, etc.), even if the diagnostic message or suggestion is in another language. Translate the suggestion before applying.

### 3. Suggestion handling

- If the analyzer provides a suggestion: use it verbatim when it fits intent and language; adapt minimally when it does not; reject it when it would alter intent, contradict another instruction, or violate the rules below.
- If no suggestion is provided: produce the smallest edit that resolves the diagnostic.

## Editing Rules

### Allowed
- Inline clarification of vague terms (definitions in parentheses or short relative clauses).
- Replacement of an ambiguous phrase with a precise equivalent.
- Removal of a clause when a contradiction diagnostic specifically calls for it.
- Adding the minimum content required to close a coverage-gap diagnostic — but only the gap, no adjacent topics.

### Forbidden
- Adding new top-level sections, headings, or rule lists not already in the file.
- Adding new instructions, sub-rules, examples, or decision trees solely to satisfy a cognitive-load diagnostic.
- Removing instructions for any reason other than a contradiction diagnostic.
- Reordering the file's overall section structure.
- Edits that change the meaning of an existing rule.

## Diagnostic-Class Playbook

- **Contradiction** — required to fix. Remove or rewrite the conflicting clause; document which side was kept and why.
- **Ambiguity** — required to fix. Apply suggestion verbatim if usable; otherwise inline a precise definition.
- **Persona conflict** — required to fix. Reconcile the conflicting role/tone clauses, keeping the one that matches the file's stated purpose.
- **Coverage gap** — fix when the gap is real and small enough to close with a single sentence or bullet inside an existing section. Otherwise reject and report.
- **Cognitive load (constraint-overload, deep-decision-tree, priority-conflict)** — fix only if the diagnostic targets a specific line that can be tightened in place (e.g., turning two competing rules into one ordered statement). Reject when the suggested fix requires new sections, new categories, or a new decision tree, since that violates the editing rules above.

## Conflict Resolution

- If two diagnostics on the same file contradict each other, prefer the fix that resolves the higher-severity class (contradiction > ambiguity > persona > coverage > cognitive load). Document the rejected one.
- If a suggestion conflicts with an existing instruction in the same file, the existing instruction wins; rewrite the suggestion or reject it.
- If a suggestion conflicts with these editing rules, the editing rules win.

## Ambiguous Match Handling

- Before editing, verify the target string is unique in the file. If the same phrase occurs more than once, only the occurrence on the diagnostic's line is in scope; use surrounding context to disambiguate the edit.
- If the occurrence cannot be disambiguated safely, reject the fix and report the ambiguity.

## Stop Conditions (Anti-Oscillation)

- Apply at most **two** rounds of fixes per file per invocation.
- Do not re-fix a diagnostic that was already addressed in the current session, even if the analyzer re-flags it with slightly different wording.
- If a fix produces a new diagnostic of the same class on the same region, revert the fix and report.
- Whole-file diagnostics (line 1 / line of first heading) cannot be patched locally; report and skip.

## Output

- Apply edits via the editor's native edit tools. A code-block dump of the full file is acceptable only as a fallback when no edit tool is available.
- Always run the analyzer again after edits and include the resulting diagnostic delta in the report.

## Reporting

For every invocation, report per diagnostic:

- **fixed** — short note on what changed.
- **rejected** — class, line, and the rule from this skill that blocked the fix.
- **deferred** — diagnostic acknowledged but not actionable in this round (e.g., whole-file cognitive load).

End with the post-edit diagnostic count.
