# Use EasyTesting agent skills in your application

The [portable agent kit](../../agent-kit/README.md) provides generic instructions and focused skills
for applications that use XBullet.EasyTesting. It supports Codex, GitHub Copilot, and Claude Code
through native instruction and skill locations while maintaining one shared source.

Use the kit to help an agent choose packages, construct authenticated integration tests, isolate
scenario state, verify application boundaries, and maintain real HTTP snapshot contracts. The
guidance preserves the consuming project's test runner, framework version, and application design.

## Choose a skill for your task

| Skill | Use it to... |
| --- | --- |
| [`easytesting-integration-tests`](../../agent-kit/skills/easytesting-integration-tests/SKILL.md) | Set up a test host, choose authentication, isolate scenario state, and assert endpoint behavior |
| [`easytesting-application-boundaries`](../../agent-kit/skills/easytesting-application-boundaries/SKILL.md) | Arrange and verify databases, outbound HTTP, messaging, telemetry, background work, and specialized dependencies |
| [`easytesting-snapshots`](../../agent-kit/skills/easytesting-snapshots/SKILL.md) | Capture actual HTTP JSON and exchanges, preserve Newtonsoft.Json contracts, redact sensitive data, and review baselines |

Each skill contains its own supporting references. Install the complete folder so the agent can
load detailed guidance when the task needs it. Combine skills when a test crosses boundaries:
an endpoint test that writes to a database and returns a snapshot can use all three.

## Install the kit

Use Python 3.9 or later. From a checkout of XBullet.EasyTesting, preview installation into your
application repository, then apply it:

```shell
python agent-kit/install.py --target /path/to/your-application --agents all --dry-run
python agent-kit/install.py --target /path/to/your-application --agents all
```

The target directory must already exist. On Windows, use a quoted Windows path; `py` can replace
`python`. Select individual agents with `--agents codex`, `--agents copilot`, or `--agents claude`.
The installer merges shared instructions and copies all three skills into each selected agent's
native project location. Existing instructions outside its managed block are preserved.

Review the resulting diff and start a fresh agent session if the new files are not discovered.
See the [installation guide](../../agent-kit/README.md#install-in-a-consuming-repository) for native
paths, conflict handling, updates, and manual installation.

## Ask an agent to use a skill

Mention the skill name together with the behavior the test should prove. For example:

- "Use easytesting-integration-tests to test GET /api/orders for an administrator, an anonymous
  caller, and a user without the required permission."
- "Use easytesting-application-boundaries to verify that creating an order saves it and publishes
  one orders.created message."
- "Use easytesting-snapshots to capture this endpoint's actual Newtonsoft.Json response and full
  HTTP exchange, with sensitive headers redacted."

Give the agent the relevant endpoint, existing test fixture, and expected behavior. Review the test
diff and validation results, including any intentional snapshot baseline changes.

## Choose how much guidance to install

- For general project rules, merge [the shared instructions](../../agent-kit/INSTRUCTIONS.md)
  into your agent's instruction file.
- For task-specific workflows, install the relevant complete skill folders, including references.
- For both, use the kit's installer with the required agent names and an explicit consuming-project
  target. Preview changes with `--dry-run` and review the resulting diff.

This is a source-distributed kit; installing the NuGet framework packages does not automatically
install agent guidance. The instructions and skills are generic to consuming applications and do
not require this repository's sample fixtures or test runner.
