# The `smash` build tool

`smash` is Atelier's build tool. It is packaged from `Atelier.Build`
(`PackAsTool`, `ToolCommandName` `smash`, `PackageId` `Atelier.Build`) and drives
the framework build, the generated test harness, boutique generation, the Docker
pipeline, and build observability (history, trends, impact, dashboards).

`dotnet build src/Atelier/Atelier.slnx` compiles the solution directly and is all
you need for an ordinary code-edit loop. Reach for `smash` when you want boutique
generation, the in-process test harness, benchmarks, the Docker pipeline, or
build telemetry on top of the raw compile.

## Running smash

From source, without installing anything:

```bash
dotnet run --project src/Atelier/Atelier.Build -- <args>
```

Or, when installed as a global tool (run `install-smash.sh`, which builds the
project and `dotnet tool install`s it):

```bash
smash <args>
```

The examples below use the `smash` form. Substitute
`dotnet run --project src/Atelier/Atelier.Build --` for the bare `smash` when
running from source.

smash locates the solution root by walking up from the working directory (and,
failing that, searching down into it) for a `.slnx`/`.sln` or `boutiques`
directory. Pass `-C <dir>` / `--path <dir>` to run from anywhere — it points
smash at the directory to operate in, e.g. `smash -C src/Atelier` from the repo
root. It applies to every verb and command.

Argument routing happens in `Program.cs`:

- No arguments runs the `smash` build verb (equivalent to `smash smash`).
- A leading build verb (`smash`, `allsmash`, `test`, `kill`) runs that verb.
- A leading analysis/utility command (`analyze`, `status`, `tree`, …) runs that
  command.
- Anything else is treated as a build target, i.e. `smash <target>`.

## Building

```bash
smash
```

Builds every discovered subsystem through the build pipeline.

```bash
smash <target>
```

Builds a single target. `<target>` is either a subsystem name (resolved by
walking the tree for boutique definitions) or a path ending in `.sln` /
`.csproj`.

Build options (passed as global flags before or after the verb):

| Flag | Effect |
|---|---|
| `--generate-boutiques` | Generate boutique projects before building |
| `--test` | Run the generated test suite after building |
| `--benchmark` | Run benchmarks after building |
| `--allow-benchmark-regression` | Do not fail the build on benchmark regressions |
| `--diagram` | Generate a mermaid architecture diagram |
| `--no-incremental` | Disable incremental build |
| `--no-coverage` | Disable code-coverage collection |
| `--skip-docker` | Skip the Docker steps in the full pipeline |
| `--force` | Skip confirmation prompts |
| `--max-nf <n>` | Maximum allowed non-functional test failures |
| `--nf-allowlist <path>` | Path to the non-functional failure allowlist |
| `--pattern <text>` | Filter target processes by command-line pattern (used by `kill`) |
| `--dry-run` | Show what would run without running it |
| `--verbose` | Verbose output |
| `-C` / `--path <dir>` | Run smash from this directory (locates the solution root there) |

Example — regenerate boutiques, build, then run the harness:

```bash
smash --generate-boutiques --test
```

## Full pipeline: `allsmash`

```bash
smash allsmash
```

Runs the end-to-end pipeline: clean all artifacts → generate boutique projects →
build boutiques → `docker-compose down -v` → `docker-compose build` →
`docker-compose up -d`. Pass `--skip-docker` to stop after the build.

## Boutiques and `smash.yml`

A boutique is a buildable unit described by a `smash.yml` file at its directory
root. smash discovers boutiques by locating these files. The benchmark and
example projects under `src/Atelier/` each ship one.

Minimal boutique:

```yaml
name: example
solution: Atelier.Example.Bench/Atelier.Example.Bench.csproj
benchmark:
  project: Atelier.Example.Bench
```

Full field reference (`SmashYamlSchema`):

| Field | Type | Notes |
|---|---|---|
| `name` | string | Required. Boutique identifier used as a `smash <target>`. |
| `solution` | string | Required. Path to the `.sln` or `.csproj` to build. |
| `description` | string | Optional human-readable description. |
| `dependencies` | string[] | Other boutiques this one depends on. |
| `build.configuration` | string | `Debug` or `Release` (default `Debug`). |
| `build.parallel` | bool | Parallel build (default `true`). |
| `build.target_framework` | string | Default `net10.0`. |
| `build.sdk_image_digest` | string | Pinned SDK base-image digest for Docker. |
| `build.runtime_image_digest` | string | Pinned runtime base-image digest for Docker. |
| `test.projects` | string[] | Test projects to run. |
| `test.output.loggers` | string[] | Test loggers. |
| `test.output.directory` | string | Test-output directory. |
| `test.coverage.enabled` | bool | Default `true`. |
| `test.coverage.threshold` | int | 0–100, default `80`. |
| `test.coverage.formats` | string[] | Default `cobertura`, `opencover`, `json`. |
| `test.coverage.html_report` | bool | Default `true`. |
| `test.coverage.exclude` / `include` | string[] | Coverage filters. |
| `benchmark.project` | string | Benchmark project to run. |
| `benchmark.output.directory` | string | Benchmark-output directory. |
| `benchmark.output.exporters` | string[] | Benchmark exporters. |
| `pre_build` / `post_build` / `post_test` | per-OS steps | Each holds `linux` / `windows` / `macos` lists of `{ name, command, working_directory, required_tools, skip_if_missing, description }`. |

## Testing

```bash
smash test [filter]
```

Runs the generated test harness in-process: it enumerates the compiled framework
assemblies, discovers `[GeneratedTest]` fixtures, executes them, and prints a
`Total / Pass / Fail / NeedsFixture` summary. See the [docs index](../README.md)
for the build and test workflow.

## Generated artifacts

`Atelier.Build` ships non-Roslyn generators under `Generation/` that emit
Dockerfiles, `docker-compose.yml`, `Program.cs` host scaffolding, and mermaid
diagrams. The `docker-compose.yml` at the repo root is a smash output —
regenerate it via a boutique-generating build rather than editing it by hand.

## Command reference

The build verbs run through the Vice CLI; the analysis and utility commands run
through System.CommandLine.

| Command | Purpose |
|---|---|
| `smash [target]` | Build boutiques, a subsystem, or generate artifacts |
| `allsmash` | Full pipeline: clean → generate → build → Docker rebuild |
| `test [filter]` | Run the generated test suite in-process |
| `kill` | Kill orphaned `dotnet` host processes (filter with `--pattern`) |
| `analyze` | Analyze build health across subsystems |
| `status` | Show build status for subsystems |
| `history` | Show build history and timeline |
| `trends` | Visualize code-quality trends |
| `dashboard` | Show the real-time build dashboard |
| `baseline` | Manage performance baselines |
| `impact` | Analyze change impact for a subsystem |
| `tree` | Visualize the subsystem dependency tree |
| `artifacts` | Browse generated artifacts (coverage, benchmarks, logs) |
| `docker` | Build and run Docker containers |
| `watch` | Watch a subsystem and rebuild on file changes |
| `unsmash` | Clean build artifacts and generated files |

Run `smash --help` for the live list, or `smash <command> --help` for a single
command's options.
