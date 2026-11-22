"""Build and verify a Cloud Build source archive from an already locked allowlist."""
import argparse
import gzip
import hashlib
import json
from pathlib import Path, PurePosixPath
import stat
import sys
import tarfile


def digest(stream):
    result = hashlib.sha256()
    for chunk in iter(lambda: stream.read(1024 * 1024), b""):
        result.update(chunk)
    return result.hexdigest()


def regular_file(root, relative):
    if not isinstance(relative, str) or not relative or "\\" in relative or ":" in relative:
        raise ValueError("Invalid archive member path")
    parts = relative.split("/")
    if any(part in ("", ".", "..") for part in parts) or any(ord(char) < 32 or ord(char) == 127 for char in relative):
        raise ValueError("Unsafe archive member path")
    if PurePosixPath(relative).is_absolute():
        raise ValueError("Archive member must be relative")
    current = root
    for index, part in enumerate(parts):
        current = current / part
        info = current.lstat()
        if stat.S_ISLNK(info.st_mode) or getattr(info, "st_file_attributes", 0) & stat.FILE_ATTRIBUTE_REPARSE_POINT:
            raise ValueError("Archive sources cannot use symlinks or reparse points")
        if index < len(parts) - 1 and not stat.S_ISDIR(info.st_mode):
            raise ValueError("Archive ancestor is not a directory")
    if not stat.S_ISREG(info.st_mode) or not current.resolve().is_relative_to(root):
        raise ValueError("Archive source must be a regular in-root file")
    return current, info


def create(root, manifest_path, output):
    root = root.resolve(strict=True)
    manifest_bytes = manifest_path.read_bytes()
    document = json.loads(manifest_bytes)
    if set(document) != {"schemaVersion", "files"} or document["schemaVersion"] != 1:
        raise ValueError("Unsupported archive manifest")
    records = document["files"]
    if not isinstance(records, list) or not records:
        raise ValueError("Archive requires a nonempty exact file manifest")
    expected = {}
    spellings = set()
    for record in records:
        if not isinstance(record, dict) or set(record) != {"path", "size", "sha256"}:
            raise ValueError("Invalid archive manifest entry")
        name, size, sha = record["path"], record["size"], record["sha256"]
        if not isinstance(name, str) or name.casefold() in spellings:
            raise ValueError("Duplicate archive member")
        if type(size) is not int or size < 0 or not isinstance(sha, str) or len(sha) != 64 or any(c not in "0123456789abcdef" for c in sha):
            raise ValueError("Invalid expected source size or hash")
        path, info = regular_file(root, name)
        if info.st_size != size or path.resolve() == output.resolve():
            raise ValueError("Source size differs from the locked manifest")
        expected[name] = (record, path, info)
        spellings.add(name.casefold())

    # No traversal: only the exact reviewed records are opened, never a directory scan.
    with output.open("xb") as raw:
        with gzip.GzipFile(filename="", mode="wb", fileobj=raw, compresslevel=1, mtime=0) as zipped:
            with tarfile.open(fileobj=zipped, mode="w", format=tarfile.PAX_FORMAT) as archive:
                for name in sorted(expected):
                    record, path, info = expected[name]
                    member = tarfile.TarInfo(name)
                    member.size = record["size"]
                    member.mode = stat.S_IMODE(info.st_mode)
                    member.mtime = 0
                    with path.open("rb") as source:
                        archive.addfile(member, source)

    seen = set()
    with tarfile.open(output, "r:gz") as archive:
        for member in archive:
            if not member.isfile() or member.name not in expected or member.name in seen:
                raise ValueError("Archive contains an unapproved, duplicate or nonregular member")
            record = expected[member.name][0]
            source = archive.extractfile(member)
            if source is None or member.size != record["size"] or digest(source) != record["sha256"]:
                raise ValueError("Archive bytes differ from the locked source manifest")
            seen.add(member.name)
    if seen != set(expected):
        raise ValueError("Archive is missing approved source files")
    with output.open("rb") as source:
        archive_sha = digest(source)
    return {"archiveSha256": archive_sha, "manifestSha256": hashlib.sha256(manifest_bytes).hexdigest(), "fileCount": len(seen)}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--manifest", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    arguments = parser.parse_args()
    try:
        print(json.dumps(create(arguments.root, arguments.manifest, arguments.output)))
    except (OSError, ValueError, TypeError, KeyError, tarfile.TarError) as error:
        print(json.dumps({"error": str(error)}))
        sys.exit(1)
