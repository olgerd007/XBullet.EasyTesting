"""Behavioral checks for portable installation, preservation, and conflict handling."""

import contextlib
import importlib.util
import io
from pathlib import Path
import sys
import tempfile
import unittest


SPEC = importlib.util.spec_from_file_location("easytesting_install", Path(__file__).parents[1] / "install.py")
INSTALL = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = INSTALL
SPEC.loader.exec_module(INSTALL)


class InstallTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.target = Path(self.temporary.name) / "consumer"
        self.target.mkdir()

    def run_install(self, *options):
        with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
            return INSTALL.main(["--target", str(self.target), *options])

    def contents(self):
        return {
            path.relative_to(self.target): path.read_bytes()
            for path in self.target.rglob("*")
            if path.is_file()
        }

    def test_all_agents_receive_self_contained_skills(self):
        self.assertEqual(0, self.run_install())
        for instruction_file, skill_directory in INSTALL.AGENTS.values():
            self.assertIn(INSTALL.BEGIN, (self.target / instruction_file).read_text(encoding="utf-8"))
            for source in (INSTALL.KIT / "skills").rglob("*"):
                if source.is_file():
                    installed = self.target / skill_directory / source.relative_to(INSTALL.KIT / "skills")
                    self.assertEqual(source.read_bytes(), installed.read_bytes())

    def test_dry_run_performs_no_writes(self):
        original = b"# Existing project rules\r\n\r\nKeep the established runner.\r\n"
        (self.target / "AGENTS.md").write_bytes(original)
        before = self.contents()
        self.assertEqual(0, self.run_install("--dry-run"))
        self.assertEqual(before, self.contents())

    def test_second_install_is_idempotent(self):
        self.assertEqual(0, self.run_install())
        before = self.contents()
        self.assertEqual([], INSTALL.plan_install(self.target, list(INSTALL.AGENTS)))
        self.assertEqual(0, self.run_install())
        self.assertEqual(before, self.contents())

    def test_update_preserves_rules_before_and_after_managed_block(self):
        prefix = b"\xef\xbb\xbf# Application rules\r\n\r\nKeep custom fixtures.\r\n\r\n"
        suffix = b"\r\n\r\n## Team conventions\r\n\r\nKeep team settings.\r\n"
        old_block = (INSTALL.BEGIN + "\r\nOld framework instructions\r\n" + INSTALL.END).encode()
        path = self.target / "AGENTS.md"
        path.write_bytes(prefix + old_block + suffix)
        self.assertEqual(0, self.run_install("--agents", "codex"))
        updated = path.read_bytes()
        self.assertTrue(updated.startswith(prefix))
        self.assertTrue(updated.endswith(suffix))
        self.assertNotIn(b"Old framework instructions", updated)
        self.assertEqual(1, updated.count(INSTALL.BEGIN.encode()))

    def test_skill_conflict_fails_before_any_write(self):
        path = self.target / ".claude/skills/easytesting-snapshots/SKILL.md"
        path.parent.mkdir(parents=True)
        path.write_text("A locally customized skill\n", encoding="utf-8")
        before = self.contents()
        self.assertEqual(1, self.run_install())
        self.assertEqual(before, self.contents())

    def test_explicit_overwrite_keeps_unrelated_files(self):
        self.assertEqual(0, self.run_install("--agents", "claude"))
        folder = self.target / ".claude/skills/easytesting-snapshots"
        (folder / "SKILL.md").write_text("Customized\n", encoding="utf-8")
        (folder / "team-reference.txt").write_text("Keep this\n", encoding="utf-8")
        self.assertEqual(0, self.run_install("--agents", "claude", "--overwrite-skills"))
        source = INSTALL.KIT / "skills/easytesting-snapshots/SKILL.md"
        self.assertEqual(source.read_bytes(), (folder / "SKILL.md").read_bytes())
        self.assertEqual("Keep this\n", (folder / "team-reference.txt").read_text(encoding="utf-8"))

    def test_single_agent_leaves_other_agent_paths_untouched(self):
        self.assertEqual(0, self.run_install("--agents", "copilot"))
        self.assertTrue((self.target / ".github/skills/easytesting-snapshots/SKILL.md").is_file())
        self.assertFalse((self.target / "AGENTS.md").exists())
        self.assertFalse((self.target / "CLAUDE.md").exists())
        self.assertFalse((self.target / ".agents").exists())
        self.assertFalse((self.target / ".claude").exists())

    def test_malformed_instruction_markers_fail_without_writes(self):
        (self.target / "AGENTS.md").write_text(INSTALL.BEGIN + "\nUnfinished\n", encoding="utf-8")
        before = self.contents()
        self.assertEqual(1, self.run_install())
        self.assertEqual(before, self.contents())

    def test_parent_file_conflict_fails_before_any_write(self):
        (self.target / ".github").write_text("Not a directory\n", encoding="utf-8")
        before = self.contents()
        self.assertEqual(1, self.run_install())
        self.assertEqual(before, self.contents())

    def test_rejects_symlink_escape(self):
        outside = Path(self.temporary.name) / "outside"
        outside.mkdir()
        try:
            (self.target / ".github").symlink_to(outside, target_is_directory=True)
        except OSError:
            self.skipTest("Creating symlinks is unavailable on this host")
        self.assertEqual(1, self.run_install())
        self.assertEqual([], list(outside.iterdir()))
        self.assertFalse((self.target / "AGENTS.md").exists())

    def test_nonexistent_target_is_not_created(self):
        missing = self.target / "missing"
        with self.assertRaises(ValueError):
            INSTALL.plan_install(missing, ["codex"])
        self.assertFalse(missing.exists())


if __name__ == "__main__":
    unittest.main()
