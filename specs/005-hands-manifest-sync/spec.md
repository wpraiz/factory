# 005 · Hands missing from the manifest

## Problem
`node tools/check-hands.mjs` fails today:

```
ERRO  mao "seguranca" existe no OdinHands mas nao no hands.json
ERRO  mao "roteiro" existe no OdinHands mas nao no hands.json
```

Both hands exist in the `OdinHands.cs` switch but have no entry (and no tags) in `src/Factory/Assets/hands.json`,
so the Setup screen and the agent tool list do not know about them.

## Goal
The four places that define a hand agree again, and the check guards it.

## Acceptance criteria
- [ ] `hands.json` has `seguranca` and `roteiro` with correct tags (read/write/exec…) and description.
- [ ] `NOMES` in `factory-orb.js` and the tool list in `tools/create-orb-agent.mjs` include them (or the hands are
      removed everywhere, if they turn out to be dead).
- [ ] `node tools/check-hands.mjs` prints `ok` and exits 0.
- [ ] The check runs in CI (privacy job) so this cannot drift again.

## Out of scope
- Changing what the hands do.
