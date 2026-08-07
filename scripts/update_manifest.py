#!/usr/bin/env python3
"""Insert or replace a version entry in the Jellyfin plugin repository manifest.

Jellyfin reads manifest.json as a list of packages; each package carries a list
of versions newest-first. The server downloads `sourceUrl`, checks its MD5
against `checksum`, and extracts the archive into plugins/<name>_<version>/.
"""

import argparse
import hashlib
import json
import pathlib
import sys
from datetime import datetime, timezone

REPO_ROOT = pathlib.Path(__file__).resolve().parent.parent
MANIFEST = REPO_ROOT / "manifest.json"


def version_key(version):
    """Sort key that orders 1.10.0.0 above 1.9.0.0."""
    parts = []
    for part in version.split("."):
        try:
            parts.append(int(part))
        except ValueError:
            parts.append(0)
    return tuple(parts)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--version", required=True, help="Plugin version, e.g. 1.0.0.0")
    parser.add_argument("--zip", required=True, type=pathlib.Path, help="Built plugin archive; its MD5 becomes the checksum")
    parser.add_argument("--source-url", required=True, help="Public download URL for the archive")
    parser.add_argument("--target-abi", required=True, help="Minimum Jellyfin version, e.g. 10.11.0.0")
    parser.add_argument("--changelog", default="", help="Changelog text for this version")
    args = parser.parse_args()

    if not args.zip.is_file():
        sys.exit(f"archive not found: {args.zip}")

    checksum = hashlib.md5(args.zip.read_bytes()).hexdigest()

    manifest = json.loads(MANIFEST.read_text())
    if not manifest:
        sys.exit("manifest.json has no package entry to add a version to")

    package = manifest[0]
    entry = {
        "version": args.version,
        "changelog": args.changelog,
        "targetAbi": args.target_abi,
        "sourceUrl": args.source_url,
        "checksum": checksum,
        "timestamp": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
    }

    # Replace any existing entry for this version so re-running a release is safe.
    versions = [v for v in package.get("versions", []) if v.get("version") != args.version]
    versions.append(entry)
    versions.sort(key=lambda v: version_key(v["version"]), reverse=True)
    package["versions"] = versions

    MANIFEST.write_text(json.dumps(manifest, indent=2) + "\n")
    print(f"added {args.version} (md5 {checksum}) to manifest.json")


if __name__ == "__main__":
    main()
