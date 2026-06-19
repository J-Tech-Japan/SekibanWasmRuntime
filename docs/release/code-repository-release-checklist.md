# Post-NuGet Code/Repository Release Checklist

This checklist is for staging the later public source/repository release after
the NuGet preview package release is ready. It is not the NuGet publish
checklist and must not be used to publish packages.

## Release Sequence Gate

- [ ] The NuGet preview release checklist has passed for the intended
  `1.0.0-preview.*` package version, or a release-blocking deferral explicitly
  names why code publication is being staged before package publish.
- [ ] The latest package inspection, consumer smoke, license/notice,
  compatibility, and public hygiene evidence is linked from
  `reports/public-release/preview-release-dry-run.md`.
- [ ] Source publication notes state whether the packages are already published
  or still blocked by the NuGet readiness gate.
- [ ] No repository release task changes package IDs, package versions, package
  metadata, or the `release-nuget-preview` publish workflow unless it also
  reruns the NuGet readiness checklist.

## Source/Repository Readiness

- [ ] `README.md` explains package selection, the ELv2 license boundary, and
  where release evidence lives.
- [ ] `CONTRIBUTING.md` states that contributions are licensed under the same
  Elastic License 2.0 terms.
- [ ] `NOTICE` lists third-party attribution for bundled or vendored code used
  by this repository.
- [ ] `docs/release/nuget-preview-release.md` remains the package publish
  operation guide; this checklist remains source/repository specific.
- [ ] Generated packages, local build outputs, and release dry-run artifacts are
  not committed unless they are durable evidence under `reports/`.
- [ ] No reserved cloud-service product branding or cloud-service product
  promise is introduced in this repository.

## License Boundary Review

- [ ] README, package README, and contribution docs consistently explain that
  users may use, modify, redistribute, and self-host SekibanWasmRuntime,
  including for internal company use.
- [ ] The same docs consistently explain that third-party hosted service,
  managed service, SaaS, or similar cloud-provider substitution requires a
  separate commercial license from J-Tech Japan.
- [ ] The source release notes do not weaken the ELv2 hosted-service restriction
  or imply that a cloud provider may offer SekibanWasmRuntime as a competing
  managed service without that license.
- [ ] Upstream Sekiban remains described as Apache-2.0 and separate from this
  repository's ELv2 license boundary.

## Final Checks

- [ ] Run the source/repository publication dry run and review the generated
  report:

  ```bash
  scripts/release/dry-run-code-publication.sh
  ```

- [ ] Run a link/path review across README, package README, CONTRIBUTING, and
  release docs.
- [ ] Confirm no reserved cloud-service product branding is present.
- [ ] Run `git diff --check`.
- [ ] Record any remaining source/repository release blockers in
  `reports/public-release/` before tagging or announcing repository
  publication.
