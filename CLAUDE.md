# Repository Guidelines (for Claude and other agents)

## Defaults

- Write communication, commit messages, and user-facing docs in English unless the user explicitly requests Japanese.
- Prefer small, reviewable changes.

## Git/GitHub Commands

- Do not run `git` or `gh` commands unless the user explicitly asks you to.
- If a `git`/`gh` command is necessary to proceed, ask first and explain what you will run.

## Branching and Commits

- Never commit directly to `main`.
- Create a feature branch for any change (example: `feat/wasm-runtime-...`, `fix/...`, `chore/...`).
- Use Conventional Commits (examples: `feat: ...`, `fix: ...`, `chore: ...`, `docs: ...`, `test: ...`).

## Testing

- Always run the most relevant tests locally before committing.
- If tests cannot be run (missing SDKs, timeouts, etc.), state exactly why and what you ran instead.

## Quality Bar

- Keep builds and tests green.
- Avoid committing generated artifacts and machine-local files.
