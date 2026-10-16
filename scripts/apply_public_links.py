"""Fill confirmed public URLs from one JSON file; never commit, tag, push or deposit."""

import argparse
import json
import re
from pathlib import Path
from urllib.parse import urlsplit

KEYS = ("repository_url", "version_url", "documentation_url")
LABELS = ("Repository", "Version 0.1.0", "Documentation")
BEGIN, END = "<!-- PUBLIC_LINKS:BEGIN -->", "<!-- PUBLIC_LINKS:END -->"


def validate_links(values):
    if set(values) != set(KEYS):
        raise ValueError("PUBLIC_LINKS.json must contain exactly: " + ", ".join(KEYS))
    for key in KEYS:
        value = values[key]
        if not isinstance(value, str) or value == "<TODO>":
            raise ValueError(f"Fill {key} with its real public HTTPS URL first")
        parsed = urlsplit(value)
        if (
            parsed.scheme != "https"
            or not parsed.hostname
            or parsed.username
            or parsed.password
            or any(c.isspace() or c in '<>"{}\\' for c in value)
        ):
            raise ValueError(f"Invalid HTTPS URL: {key}")
    repository = urlsplit(values["repository_url"])
    parts = repository.path.strip("/").removesuffix(".git").split("/")
    if repository.hostname != "github.com" or len(parts) != 2 or not all(parts):
        raise ValueError("repository_url must name a real github.com/OWNER/REPOSITORY")
    version = urlsplit(values["version_url"])
    prefix = "/" + "/".join(parts) + "/"
    if version.hostname != "github.com" or not version.path.startswith(prefix):
        raise ValueError("version_url must identify a version within that GitHub repository")
    suffix = version.path[len(prefix) :]
    if not any(
        suffix.startswith(kind) and len(suffix) > len(kind)
        for kind in ("tree/", "releases/tag/", "commit/")
    ):
        raise ValueError("Use a version-specific tree, release tag or commit URL")
    return values


def replace_block(text, values):
    block = (
        BEGIN
        + "\n"
        + "\n".join(
            f"- {label}: [{value}]({value})"
            for label, value in zip(LABELS, (values[k] for k in KEYS), strict=True)
        )
        + "\n"
        + END
    )
    pattern = re.escape(BEGIN) + r".*?" + re.escape(END)
    if len(re.findall(pattern, text, re.S)) != 1:
        raise ValueError("Expected exactly one PUBLIC_LINKS block")
    return re.sub(pattern, lambda _: block, text, flags=re.S)


def build_plan(root, values):
    values = validate_links(values)
    plan = {}
    for name in ("README.md", "RELEASE_NOTES.md"):
        path = root / name
        plan[path] = replace_block(path.read_text(encoding="utf-8"), values)
    citation = root / "CITATION.cff"
    cff = citation.read_text(encoding="utf-8")
    for key, value in (
        ("repository-code", values["repository_url"]),
        ("repository-artifact", values["version_url"]),
        ("url", values["documentation_url"]),
    ):
        pattern = r"(?m)^#?\s*" + re.escape(key) + r":.*$"
        replacement = key + ": " + json.dumps(value)
        if re.search(pattern, cff):
            cff = re.sub(pattern, lambda _, replacement=replacement: replacement, cff)
        else:
            cff += replacement + "\n"
    plan[citation] = cff
    metadata = root / "pyproject.toml"
    toml = metadata.read_text(encoding="utf-8")
    begin, end = "# PUBLIC_LINKS:BEGIN", "# PUBLIC_LINKS:END"
    block = (
        begin
        + "\n[project.urls]\n"
        + "\n".join(
            json.dumps(label) + " = " + json.dumps(values[key])
            for key, label in zip(KEYS, LABELS, strict=True)
        )
        + "\n"
        + end
    )
    pattern = re.escape(begin) + r".*?" + re.escape(end)
    if len(re.findall(pattern, toml, re.S)) != 1:
        raise ValueError("Expected one PUBLIC_LINKS block in pyproject.toml")
    plan[metadata] = re.sub(pattern, lambda _: block, toml, flags=re.S)
    return plan


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    values = json.loads((root / "PUBLIC_LINKS.json").read_text(encoding="utf-8"))
    try:
        plan = build_plan(root, values)
    except ValueError as error:
        parser.error(str(error))
    # Validate everything before writing; retain recoverable pre-update backups.
    if not args.dry_run:
        for path in plan:
            backup = path.with_name(path.name + ".before-public-links")
            if not backup.exists() and path.exists():
                backup.write_bytes(path.read_bytes())
        for path, content in plan.items():
            path.write_text(content, encoding="utf-8", newline="\n")
    for path in plan:
        print(("Would update: " if args.dry_run else "Updated: ") + str(path))
    print("URL format checked only; verify public access yourself.")
    print("No commit, tag, remote or push performed.")


if __name__ == "__main__":
    main()
