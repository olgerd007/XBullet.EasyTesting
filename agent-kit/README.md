# Portable EasyTesting agent kit

Use these generic instructions and skills in any application that consumes XBullet.EasyTesting.
The kit does not depend on this repository's solution, sample application, fixtures, or runner.
It changes agent guidance only; it does not install NuGet packages or change production settings.

## Contents

- [Shared instructions](INSTRUCTIONS.md): package selection, test boundaries, isolation,
  serialization, and validation rules for all agents.
- [Integration-test skill](skills/easytesting-integration-tests/SKILL.md): hosts, identities,
  scopes, service overrides, and endpoint assertions.
- [Application-boundary skill](skills/easytesting-application-boundaries/SKILL.md): database,
  HTTP, messaging, telemetry, background completion, and specialized dependencies.
- [Snapshot skill](skills/easytesting-snapshots/SKILL.md): JSON, controller and exchange contracts,
  Newtonsoft.Json output, recording, redaction, and baseline maintenance.

Each skill includes its own supporting references and uses common `name` and `description`
frontmatter. Framework-specific examples identify assumptions to adapt to the consuming project.
Upstream documentation links track `main`; check the installed package version before using an API.

## Install in a consuming repository

Use Python 3.9 or later; the installer uses only the standard library. From this repository:

```shell
python agent-kit/install.py --target /path/to/your-application --agents all --dry-run
python agent-kit/install.py --target /path/to/your-application --agents all
```

On Windows, `py` can replace `python`, and the target can be a quoted Windows path. Select a subset
with `--agents codex`, `--agents copilot`, `--agents claude`, or multiple names.

| Agent | Instructions | Skills |
| --- | --- | --- |
| Codex | Managed block in `AGENTS.md` | `.agents/skills/easytesting-*/` |
| GitHub Copilot | Managed block in `.github/copilot-instructions.md` | `.github/skills/easytesting-*/` |
| Claude Code | Managed block in `CLAUDE.md` | `.claude/skills/easytesting-*/` |

The installer copies complete skill folders; there are no symlinks or references back to this
checkout. Existing instructions outside the EasyTesting markers are preserved. Rerunning the same
kit is idempotent. A differing skill file is a preflight conflict: inspect the diff, then use
`--overwrite-skills` if replacement is intended. This flag affects only bundled skill files;
unrelated files are retained. Updating the instruction block replaces its content, so keep local
rules outside the markers. `--dry-run` checks conflicts and lists changes without writing.

Review and commit the installed instructions and skills in the consuming repository when they
should be shared with the team. No user-global installation or live agent session is modified.
Start a fresh agent session if the installed files are not discovered by an existing session.

## Manual installation

Copy each desired complete directory from `skills/` into the selected agent's skills folder.
Merge the relevant content from `INSTRUCTIONS.md` into the agent's instruction file. If only
generic instructions are wanted, merge that file without installing skills.

Copilot also documents `.agents/skills` and `.claude/skills` as project locations. The installer
uses each agent's native location for compatibility across clients. When multiple agents are
installed, Copilot may see equivalent copies; keep them identical and avoid divergent customization.

The discovery paths follow the
[Codex skills documentation](https://learn.chatgpt.com/docs/build-skills),
[GitHub Copilot skills documentation](https://docs.github.com/en/copilot/how-tos/copilot-on-github/customize-copilot/customize-cloud-agent/add-skills),
and [Claude Code skills documentation](https://code.claude.com/docs/en/skills).
Codex uses [AGENTS.md instructions](https://learn.chatgpt.com/docs/agent-configuration/agents-md).
Client versions, organization policy, and enabled features can affect discovery; this kit does not
change those settings or claim to have exercised hosted agent sessions.

## Example requests

- "Use easytesting-integration-tests to test this endpoint's success, anonymous, and forbidden cases."
- "Use easytesting-application-boundaries to verify the database write and outbound HTTP request."
- "Use easytesting-snapshots to capture the real Newtonsoft.Json response and complete exchange."

Mention the skill by name, or let an agent select it from its description. Codex supports explicit
`$easytesting-snapshots` invocation; Claude Code exposes `/easytesting-snapshots`. For Copilot,
reference the skill name in the request; discovery depends on the client.

## Validate changes to the kit

```shell
python -m unittest discover -s agent-kit/tests -v
```

The installer tests cover complete copies, dry runs, repeat installation, preservation, conflicts,
explicit replacement, agent selection, malformed markers, and destination containment. They do
not prove model behavior. Check skill frontmatter with a skill validator, verify Markdown links,
and check API guidance against framework tests whenever public APIs change.
