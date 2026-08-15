# Consumer-facing release version policy

The release lane checks the instructions that reach consumers, including the
README copied into each produced NuGet package. The check is driven by inline
assertion markers rather than a filename allowlist:

- `&lt;!-- release-lane: current-package-version --&gt;` marks a current package
  version that must equal the version supplied to the lane (`PACKAGE_VERSION`).
- `&lt;!-- release-lane: current-dcb-version --&gt;` marks a current Sekiban.Dcb
  baseline that must equal the concrete dependency version found in the
  produced nupkg.
- `&lt;!-- release-lane: current-runtime-image-version --&gt;` marks the current
  runtime-host tag. The lane input is the registry-verified tag, not a Git tag
  or another document.

The marker must be on the same line as the version it asserts. The check
discovers marked tracked Markdown files, then applies the same assertions to
the `README.md` extracted from each nupkg. A deliberately wrong marked fixture
must fail and name the file and line.

Point-in-time release evidence is intentionally unmarked. It records what was
true for an earlier release and is not rewritten or inspected by this current
state check. When adding a new current consumer document, add the appropriate
marker; when adding historical evidence, leave it unmarked and label its
historical scope in the document itself.

The package version and Sekiban.Dcb baseline are therefore compared against
the artifact being packed. The runtime image is a separate release lane; its
current value is admitted only with registry evidence recorded alongside the
consumer documentation.
