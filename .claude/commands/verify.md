---
description: Verify the current change against its acceptance criteria, with evidence
argument-hint: [spec folder]
---

Verify: $ARGUMENTS (if empty, the spec matching the current branch)

1. Run and read the output of each:
   - `dotnet build src/Factory/Factory.csproj -c Debug -p:Platform=x64` (0 errors, no new warnings)
   - `node tools/privacy-check.mjs --tudo` (0 blocks; review any warning)
   - `node tools/check-hands.mjs`
   - `dotnet test` if a test project exists
2. For every acceptance criterion in `spec.md`, record the evidence in the Evidence section of `tasks.md`: the
   command and its result, a screenshot path, or a commit hash. A criterion without evidence is not met.
3. Report a table: criterion · met / not met · evidence. List anything not verified and why.
4. Only if something was learned **and** there is evidence for it, add an entry at the top of `docs/LESSONS.md`
   (`date · area · lesson`, `evidence:`, `apply:`). No evidence, no entry.
