# Agent Working Agreement

This repository expects agents to follow these rules when making changes.

## Communication

- Default to English for explanations, file contents, and commit messages.
- If the user requests Japanese output, follow that request.

## Safety With Tooling

- Do not run `git` or `gh` unless explicitly requested by the user.
- If you need to run them to proceed, ask for confirmation first.

## Git Workflow

- Do not commit on `main`.
- Work on a topic branch and commit there.

## Verification

- Always run relevant tests before committing (for example `dotnet test`).
- If the repository contains multiple ecosystems, run the targeted suite(s) for what you changed.

## Aspire And Browser Tooling

- This repository includes repo-local Aspire agent configuration for MCP-capable tools.
- For Aspire work in a git worktree, prefer `aspire start --isolated` to avoid shared local state and port collisions.
- When browser automation is needed, prefer the globally installed `playwright-cli` or the repo Playwright tests under `e2e/playwright/`.

## Hygiene

- Do not commit OS/editor artifacts (for example `.DS_Store`).
- Prefer minimal diffs and keep changes scoped to the requested task.
