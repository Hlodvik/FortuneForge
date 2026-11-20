"""Report the local Graphify installation and code graph without external calls."""

from __future__ import annotations

import argparse
from collections import Counter
from importlib.metadata import version
import json
from pathlib import Path
import sys


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("check-python", "status"))
    parser.add_argument("graph", nargs="?", type=Path)
    args = parser.parse_args()

    if args.action == "check-python":
        if not (3, 11) <= sys.version_info[:2] < (3, 15):
            raise SystemExit("Graphify requires Python 3.11 through 3.14.")
        return

    print(f"Graphify {version('graphifyy')}")
    if args.graph is None or not args.graph.is_file():
        return
    graph = json.loads(args.graph.read_text(encoding="utf-8"))
    nodes = graph.get("nodes", [])
    edges = graph.get("edges", graph.get("links", []))
    print(f"Nodes: {len(nodes)}; edges: {len(edges)}")
    repository_root = Path(__file__).resolve().parents[2]
    sources: set[Path] = set()
    for node in nodes:
        source = node.get("source_file", node.get("file", ""))
        if not isinstance(source, str) or not source:
            continue
        source_path = Path(source)
        if not source_path.is_absolute():
            source_path = repository_root / source_path
        if source_path.is_file():
            sources.add(source_path)
    counts = Counter(source.suffix.lower() for source in sources)
    print("Source files with graph nodes: " + ", ".join(f"{ext}: {count}" for ext, count in counts.most_common()))


if __name__ == "__main__":
    main()
