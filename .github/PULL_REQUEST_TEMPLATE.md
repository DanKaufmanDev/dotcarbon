## What & why

<!-- What does this change, and what problem does it solve? Link any issue with "Closes #123". -->

## How it was verified

<!-- The tests you added, and any manual verification for behavior CI can't run (real signing,
device installs, etc.). "Ran the Release/warnaserror build and the full test suite" is the baseline. -->

## Checklist

- [ ] Tests added or updated (a test that fails without this change where practical)
- [ ] `dotnet build DotCarbon.slnx -c Release -p:TreatWarningsAsErrors=true -p:NuGetAudit=false` is clean
- [ ] `dotnet test DotCarbon.slnx` passes
- [ ] Docs updated for user-facing changes; command reference regenerated if a command surface changed
- [ ] Commit messages follow Conventional Commits and the PR is labeled for release notes
