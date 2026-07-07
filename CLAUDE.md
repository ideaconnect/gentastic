# CLAUDE.md

**Read [AGENTS.md](AGENTS.md).** It is the single source of truth for how Gentastic works, how the
image-generation pipeline (sampling, VAE, guidance) fits together, the model catalog, the build and
release story, and the extension recipes. Everything an agent needs to work in this repo lives there;
this file only adds the few conventions specific to working here.

For a concept-first, human tour, see [HUMANS.md](HUMANS.md).

## Working conventions in this repo

- **Environment is Windows.** The primary shell is PowerShell; a Bash tool (POSIX) is also available.
  Use forward slashes and absolute paths where you can.
- **.NET 10.** Build/test/run against the `Gentastic.slnx` solution:
  `dotnet build Gentastic.slnx -c Debug`, `dotnet test Gentastic.slnx`,
  `dotnet run --project src/Gentastic.App`.
- **Plain hyphens only** in code and docs - never em-dashes or en-dashes.
- **Match the surrounding style.** The codebase leans on file-scoped namespaces, records for data,
  `sealed` classes, source-generated MVVM (`[ObservableProperty]` / `[RelayCommand]`), and dense,
  explanatory comments that capture the *why* (especially around the native-backend and Vulkan
  quirks). Keep that voice.
- **Don't fight the native quirks.** Before changing the engine or adding a model, read section 14
  ("Load-bearing gotchas") of AGENTS.md - the backend-enable ordering, `WithVaeOnCpu`, and the bf16
  GGUF trap are easy to break and hard to debug.
- **Verify with the real app when you can.** The headless hooks (`GENTASTIC_AUTOGEN`,
  `GENTASTIC_SCREENSHOT`) and `tools/Gentastic.Smoke` drive a genuine generation - see AGENTS.md
  section 13.
- **Commit/push only when asked.** The default branch is `main`; work happens on `feat/foundation`.
