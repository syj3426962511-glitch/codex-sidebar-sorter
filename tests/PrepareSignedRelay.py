"""Read and extract the official signed Node host in disposable cloud storage."""

import hashlib
import json
import os
import pathlib
import subprocess
import tarfile

if os.environ.get("GITHUB_ACTIONS") != "true":
    raise RuntimeError("Use disposable cloud storage for native host investigation.")

root = pathlib.Path(os.environ["RUNNER_TEMP"]) / "sorter-signed-relay"
receipt = root / "host.json"
if receipt.exists():
    print(receipt.read_text())
    raise SystemExit(0)
root.mkdir(parents=True, exist_ok=True)
manifest_url = "https://persistent.oaistatic.com/codex-primary-runtime/latest/darwin-arm64/LATEST.json"
manifest = json.loads(subprocess.check_output(["/usr/bin/curl", "--fail", "--location", "--retry", "2", "--max-time", "30", manifest_url], text=True))
archive = root / "runtime.tar.xz"
digest = hashlib.sha256()
length = 0
download = subprocess.Popen(["/usr/bin/curl", "--fail", "--location", "--retry", "2", "--max-time", "180", manifest["archiveUrl"]], stdout=subprocess.PIPE)
with archive.open("wb") as output:
    while block := download.stdout.read(1024 * 1024):
        output.write(block)
        digest.update(block)
        length += len(block)
if download.wait() != 0:
    raise RuntimeError("The official runtime download failed.")
if length != manifest["archiveSizeBytes"] or digest.hexdigest() != manifest["archiveSha256"]:
    raise RuntimeError("The official runtime archive did not match its HTTPS manifest.")
with tarfile.open(archive, "r:xz") as package:
    members = package.getmembers()
    nodes = [item for item in members if item.isfile() and item.name.endswith("/node") and "/node_modules/" not in item.name]
    if len(nodes) != 1:
        raise RuntimeError("Expected one native Node host, found " + json.dumps([item.name for item in nodes]))
    node = nodes[0]
    prefix = node.name.rsplit("/bin/node", 1)[0] + "/" if node.name.endswith("/bin/node") else str(pathlib.PurePosixPath(node.name).parent) + "/"
    selected = [item for item in members if item.name.startswith(prefix)]
    package.extractall(root, members=selected, filter="data")
    licenses = [item.name for item in selected if any(term in pathlib.PurePosixPath(item.name).name.lower() for term in ("license", "notice"))]
executable = root / node.name
subprocess.run(["/usr/bin/codesign", "--verify", "--strict", str(executable)], check=True, capture_output=True)
signature = subprocess.run(["/usr/bin/codesign", "-dv", "--verbose=4", "-r-", str(executable)],
                           check=True, capture_output=True, text=True).stderr
result = {"node": str(executable), "bundleVersion": manifest["bundleVersion"],
          "signature": signature, "licensePaths": licenses, "manifestUrl": manifest_url}
receipt.write_text(json.dumps(result, indent=2))
print(receipt.read_text())

