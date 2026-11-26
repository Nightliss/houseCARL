#!/usr/bin/env bash
# build-plugin.sh - Linux sibling of build-plugin.ps1: assemble the houseCARL plugin into dist/ with the same layout.
#
# The server is published linux-x64, framework-dependent (trimming stays OFF: houseCARL is reflection-driven) and runs
# as `dotnet housecarl-mcp.dll`, so .NET 10 must be on PATH. Differences from the ps1: no setup exe, START-HERE note or
# release zip (Windows installer artefacts), no notices regeneration, and CHANGELOG.md is copied as is.
#
# Usage: scripts/build-plugin.sh [--plugin-tree-only]
set -euo pipefail

PLUGIN_TREE_ONLY=0
[[ "${1:-}" == "--plugin-tree-only" ]] && PLUGIN_TREE_ONLY=1

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PKG_ROOT="$REPO_ROOT/dist"
DIST_ROOT="$PKG_ROOT/housecarl"
SERVER_DIR="$DIST_ROOT/server"
SKILLS_DIR="$DIST_ROOT/skills"
PLUGIN_SRC="$REPO_ROOT/plugin"
GENERATED_DIR="$REPO_ROOT/generated"
REF_DIR="$REPO_ROOT/.claude/skills/mutagen-reference/references"
PLUGIN_MANIFEST="$PLUGIN_SRC/.claude-plugin/plugin.json"

SKILLS=(mutagen-reference papyrus-reference skypatcher-authoring spid-authoring kid-authoring open-animation-replacer skse-plugin-authoring)

step() { printf '\n=== [%s] %s ===\n' "$1" "$2"; }
die() { echo "error: $*" >&2; exit 1; }

[[ -f "$PLUGIN_MANIFEST" ]] || die "plugin manifest not found: $PLUGIN_MANIFEST"
VERSION="$(grep -oP '"version"\s*:\s*"\K[^"]+' "$PLUGIN_MANIFEST" | head -n1)"
[[ -n "$VERSION" ]] || die "could not read 'version' from $PLUGIN_MANIFEST"
echo "Building houseCARL v$VERSION"

step 0/8 "Clean dist/"
rm -rf "$PKG_ROOT"
mkdir -p "$DIST_ROOT"

if [[ $PLUGIN_TREE_ONLY -eq 0 ]]; then
  step 1/8 "Regenerate corpus.json (generator)"
  dotnet run --project "$REPO_ROOT/src/housecarl-generator" -c Release -- "$GENERATED_DIR" "$REF_DIR"
  [[ -f "$GENERATED_DIR/corpus.json" ]] || die "corpus not produced at $GENERATED_DIR/corpus.json"

  step 2/8 "Publish server (Release, linux-x64, framework-dependent)"
  dotnet publish "$REPO_ROOT/src/housecarl-mcp" -c Release -r linux-x64 --self-contained false -p:Version="$VERSION" -o "$SERVER_DIR"
  [[ -f "$SERVER_DIR/housecarl-mcp.dll" ]] || die "server dll not produced at $SERVER_DIR/housecarl-mcp.dll"
  find "$SERVER_DIR" -maxdepth 1 -name 'appsettings*.json' -delete

  step 3/8 "Copy corpus.json beside the server"
  cp "$GENERATED_DIR/corpus.json" "$SERVER_DIR/corpus.json"
fi

step 4/8 "Bundle skills"
mkdir -p "$SKILLS_DIR"
for s in "${SKILLS[@]}"; do
  [[ -d "$REPO_ROOT/.claude/skills/$s" ]] || die "skill not found: $s"
  cp -r "$REPO_ROOT/.claude/skills/$s" "$SKILLS_DIR/$s"
done
find "$SKILLS_DIR" -type d -name evals -prune -exec rm -rf {} +
find "$SKILLS_DIR" -type f -name '_CORPUS_STATUS.md' -delete

step 5/8 "Copy plugin files"
cp -r "$PLUGIN_SRC/.claude-plugin" "$DIST_ROOT/.claude-plugin"
for f in LICENSE THIRD-PARTY-NOTICES.txt README.md CHANGELOG.md; do cp "$PLUGIN_SRC/$f" "$DIST_ROOT/$f"; done
cat > "$DIST_ROOT/.mcp.json" <<'JSON'
{
  "mcpServers": {
    "housecarl": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["${CLAUDE_PLUGIN_ROOT}/server/housecarl-mcp.dll"],
      "env": {
        "HouseCarl__Mo2InstanceDir": "${user_config.mo2_instance_dir}",
        "HouseCarl__AmethystProfileDir": "${user_config.amethyst_profile_dir}",
        "HOUSECARL_DATA_DIR": "${CLAUDE_PLUGIN_DATA}"
      }
    }
  }
}
JSON

step 6/8 "Leak-check skills (excluded files + markdown pointers)"
fail=0
while IFS= read -r f; do echo "  LEAK (excluded file present): $f"; fail=1; done < <(
  find "$SKILLS_DIR" \( -type f -name '_CORPUS_STATUS.md' -o -type d -name evals \))
doc_count=0
for skill_dir in "$SKILLS_DIR"/*/; do
  skill="$(basename "$skill_dir")"
  while IFS= read -r doc; do
    doc_count=$((doc_count + 1))
    rel="${doc#"$skill_dir"}"
    if [[ ! -s "$doc" ]]; then echo "  SKILL DOC: $skill ships an empty $rel."; fail=1; continue; fi
    while IFS= read -r hit; do
      ptr="${hit%%[.,;:)]}"
      while [[ "$ptr" =~ [.,\;:\)]$ ]]; do ptr="${ptr%?}"; done
      if [[ "$ptr" == *\\* ]]; then echo "  SKILL DOC: $skill ($rel) writes $ptr with a backslash; skill pointers use forward slashes."; fail=1; continue; fi
      leaf="${ptr##*/}"
      [[ "$ptr" == */ || "$leaf" != *.* ]] && continue
      if [[ "$ptr" == evals/* || "$leaf" == "_CORPUS_STATUS.md" ]]; then
        echo "  SKILL DOC: $skill ($rel) points at $ptr, which this script strips from every shipped copy."; fail=1
      elif [[ ! -e "$skill_dir/$ptr" ]]; then
        echo "  SKILL DOC: $skill ($rel) points at $ptr, which is not in the shipped copy."; fail=1
      fi
    done < <(grep -oPi '(?<![A-Za-z0-9_./\\-])(?:references|evals)[/\\][A-Za-z0-9_./\\-]+|(?<![A-Za-z0-9_./\\-])_CORPUS_STATUS\.md' "$doc" || true)
    while IFS= read -r name; do
      [[ "$name" == "_CORPUS_STATUS.md" ]] && continue
      if [[ -z "$(find "$skill_dir" -type f -name "$name" -print -quit)" ]]; then
        echo "  SKILL DOC: $skill ($rel) points at $name, which is not in the shipped copy."; fail=1
      fi
    done < <(grep -oPi '(?<![A-Za-z0-9_./\\-])[A-Za-z0-9_-]+\.(?:md|jsonl)(?![A-Za-z0-9])' "$doc" | sort -u || true)
  done < <(find "$skill_dir" -type f -name '*.md')
done
[[ $fail -eq 0 ]] || die "skill leak-check failed"
echo "skill leak-check clean (${#SKILLS[@]} skills, $doc_count markdown files)."

if [[ $PLUGIN_TREE_ONLY -eq 1 ]]; then
  echo "Plugin tree assembled: $DIST_ROOT (server and marketplace skipped)"
  exit 0
fi

step 7/8 "Marketplace descriptor"
mkdir -p "$PKG_ROOT/.claude-plugin"
cp "$REPO_ROOT/packaging/marketplace.json" "$PKG_ROOT/.claude-plugin/marketplace.json"

step 8/8 "Leak-check assembled tree"
fail=0
while IFS= read -r f; do echo "  LEAK (excluded file present): $f"; fail=1; done < <(
  find "$PKG_ROOT" \( -type f \( -name 'appsettings*.json' -o -name '_CORPUS_STATUS.md' \) -o -type d -name evals \))
while IFS= read -r f; do echo "  PATH LEAK: $f"; fail=1; done < <(
  grep -rlIE --include='*.json' --include='*.jsonl' --include='*.md' --include='*.txt' --include='*.yml' --include='*.yaml' \
    -e 'C:\\+Users\\+[^\\/'"'"']+' -e '[A-Z]:\\+Steam' "$PKG_ROOT" || true)
[[ $fail -eq 0 ]] || die "leak-check failed"
echo "leak-check clean."

echo
echo "houseCARL v$VERSION assembled: $DIST_ROOT"
echo "  files: $(find "$DIST_ROOT" -type f | wc -l)   skills: ${#SKILLS[@]}"
echo "Next: claude plugin validate \"$DIST_ROOT\" --strict"
