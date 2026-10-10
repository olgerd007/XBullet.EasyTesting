#!/usr/bin/env python3
"""Install the portable EasyTesting instructions and skills into a consuming repository."""

from __future__ import annotations

import argparse
from dataclasses import dataclass
from pathlib import Path
import re
import sys


AGENTS = {
    "codex": ("AGENTS.md", ".agents/skills"),
    "copilot": (".github/copilot-instructions.md", ".github/skills"),
    "claude": ("CLAUDE.md", ".claude/skills"),
}
BEGIN = "<!-- xbullet-easytesting:begin -->"
END = "<!-- xbullet-easytesting:end -->"
KIT = Path(__file__).resolve().parent


@dataclass(frozen=True)
class Change:
    path: Path
    content: bytes


def instruction_content(existing: bytes, instructions: str) -> bytes:
    """Replace only our managed block, preserving all surrounding text and line endings."""
    text = existing.decode("utf-8")
    newline = "\r\n" if "\r\n" in text else "\n"
    body = re.sub(r"(?m)^(#{1,5}) ", r"\1# ", instructions.strip())
    block = (BEGIN + "\n" + body + "\n" + END).replace("\n", newline)
    if BEGIN in text or END in text:
        if text.count(BEGIN) != 1 or text.count(END) != 1:
            raise ValueError("Instruction file has duplicate or incomplete EasyTesting markers")
        pattern = re.compile(
            r"(?m)^" + re.escape(BEGIN) + r"\r?\n.*?^" + re.escape(END) + r"(?=\r?$)",
            re.DOTALL,
        )
        if not pattern.search(text):
            raise ValueError("Instruction file has malformed EasyTesting markers")
        text = pattern.sub(lambda _: block, text, count=1)
    else:
        separator = "" if not text else (newline if text.endswith(newline) else newline * 2)
        text += separator + block + newline
    return text.encode("utf-8")


def checked_path(root: Path, relative: Path) -> Path:
    """Refuse existing symlinks or junctions that redirect installation outside the target."""
    destination = root / relative
    try:
        destination.resolve().relative_to(root)
    except ValueError as error:
        raise ValueError(f"Destination escapes the target repository: {relative}") from error
    if destination.exists() and not destination.is_file():
        raise ValueError(f"Destination is not a file: {relative}")
    for parent in destination.parents:
        if parent == root:
            break
        if parent.exists() and not parent.is_dir():
            raise ValueError(f"Destination parent is not a directory: {parent.relative_to(root)}")
    return destination


def plan_install(
    target: Path,
    agents: list[str],
    overwrite_skills: bool = False,
    kit: Path = KIT,
) -> list[Change]:
    """Preflight every logical conflict before performing any writes."""
    target = target.resolve()
    if not target.is_dir():
        raise ValueError("Target must be an existing consuming-project directory")
    instructions = (kit / "INSTRUCTIONS.md").read_text(encoding="utf-8")
    skills = sorted((kit / "skills").glob("*/SKILL.md"))
    if not skills:
        raise ValueError("The kit has no skills to install")
    changes = []
    for agent in dict.fromkeys(agents):
        instruction_file, skill_directory = AGENTS[agent]
        path = checked_path(target, Path(instruction_file))
        existing = path.read_bytes() if path.exists() else b""
        content = instruction_content(existing, instructions)
        if content != existing:
            changes.append(Change(path, content))
        for entrypoint in skills:
            skill = entrypoint.parent
            for source in sorted(skill.rglob("*")):
                if not source.is_file():
                    continue
                relative = Path(skill_directory) / skill.name / source.relative_to(skill)
                path = checked_path(target, relative)
                content = source.read_bytes()
                if path.exists():
                    if path.read_bytes() == content:
                        continue
                    if not overwrite_skills:
                        raise ValueError(
                            f"Existing skill differs: {relative}. Review it, then use "
                            "--overwrite-skills to replace only the bundled files."
                        )
                changes.append(Change(path, content))
    return changes


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--target", type=Path, required=True, help="Existing consuming repository")
    parser.add_argument("--agents", nargs="+", choices=[*AGENTS, "all"], default=["all"])
    parser.add_argument("--dry-run", action="store_true", help="Preflight and list changes without writing")
    parser.add_argument("--overwrite-skills", action="store_true", help="Replace differing bundled skill files")
    args = parser.parse_args(argv)
    agents = list(AGENTS) if "all" in args.agents else args.agents
    try:
        changes = plan_install(args.target, agents, args.overwrite_skills)
        for change in changes:
            print(f"{'Would write' if args.dry_run else 'Write'} {change.path}")
            if not args.dry_run:
                change.path.parent.mkdir(parents=True, exist_ok=True)
                change.path.write_bytes(change.content)
        if not changes:
            print("EasyTesting instructions and skills are already current.")
        return 0
    except (OSError, UnicodeError, ValueError) as error:
        print(f"Installation failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
