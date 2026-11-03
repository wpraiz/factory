# Rules: privacy

The repository is the product; the owner's life is not. See `docs/adr/0002-repo-is-product-profile-is-life.md`.

- `profile.manifest.json` (in the repo) declares what may never enter git: file classes, globs, content rules.
  Each owner adds a private extension (`perfil.local.json`) in their profile folder (`FACTORY_PROFILE`), outside git.
- `tools/privacy-check.mjs` enforces both. It runs as pre-commit, pre-push and in CI. **Blocks** fail; **warnings**
  (personal narrative) need a human look and pass only with `PRIVACIDADE_ACEITO=1` when they are technical.
- Fixtures, examples, screenshots and docs use the fictional demo projects. No real person, client, address,
  e-mail, conversation, amount of money or local path.
- Never add an exception to the manifest to make a commit pass. Fix the content.
- Logs and evals written by the app go to `FACTORY_DATA`, never into the repo.
