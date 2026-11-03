## Spec
<!-- Link to specs/NNN-.../spec.md, or "trivial change" with one line why it needs no spec. -->

## What changed

## How verified
- [ ] `dotnet build src/Factory/Factory.csproj -c Debug -p:Platform=x64`
- [ ] `node tools/privacy-check.mjs --tudo`
- [ ] `node tools/check-hands.mjs` (if hands were touched)
- [ ] Exercised in demo mode (what was checked):

## Checklist
- [ ] No personal data (names, clients, paths, conversations, money) and no secrets
- [ ] Hands contract: if a hand changed, `OdinHands.cs`, `hands.json`, `NOMES` in `factory-orb.js` and
      `create-orb-agent.mjs` all agree
- [ ] Docs, rules or ADR updated if behaviour or a decision changed
- [ ] Lesson added to `docs/LESSONS.md` only if backed by evidence
