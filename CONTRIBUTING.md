# Contributing

Thanks for your interest in SekibanWasmRuntime.

## License of contributions

This project is distributed under the Elastic License 2.0 (see [LICENSE](LICENSE)).
By submitting a pull request, issue, or any other contribution you agree that
your contribution is licensed under the same Elastic License 2.0 and you have
the right to grant that license.

If you cannot agree to those terms, please do not submit contributions.

The same license boundary described in [README.md](README.md) applies to
contributions: SekibanWasmRuntime may be used, modified, redistributed, and
self-hosted, including for internal company use, but third-party hosted service,
managed service, SaaS, or similar cloud-provider substitution requires a
separate commercial license from J-Tech Japan.

Public release signoff for that boundary is tracked in
[reports/public-release/license-boundary-commercial-use-signoff.md](reports/public-release/license-boundary-commercial-use-signoff.md).

## Working guidelines

See [CLAUDE.md](CLAUDE.md) for the repository's working conventions (branching,
commits, testing, and agent tooling). Highlights:

- Never commit directly to `main`.
- Use Conventional Commits (`feat: ...`, `fix: ...`, `chore: ...`, etc.).
- Run the most relevant tests locally before opening a PR.
- Keep builds and tests green.

## Reporting issues

Please file issues on GitHub with enough detail to reproduce (sample name,
AppHost output, OS, `dotnet --info` if relevant).
