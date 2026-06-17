# Migration Notes

This file is the source of truth for public preview migration guidance.

See [`versioning-and-changelog.md`](versioning-and-changelog.md) for the rules
that decide when a migration note is required.

## Unreleased

No breaking public contract change is introduced by the release process
documentation update.

## Template For Breaking Changes

Use this shape when a public preview release changes consumer-visible behavior:

```markdown
## 1.0.0-preview.<n>

- Affected packages:
- Changed public contract:
- Required consumer action:
- Compatibility evidence:
- Known fallback:
```
