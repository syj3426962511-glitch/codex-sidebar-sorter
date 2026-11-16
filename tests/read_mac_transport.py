import json
import os
import pathlib
import re
import struct
import sys

if os.environ.get("GITHUB_ACTIONS") != "true" or sys.platform != "darwin":
    raise RuntimeError("Read shipping transport code on the disposable cloud Mac.")

archive, output = map(pathlib.Path, sys.argv[1:3])
data = archive.read_bytes()
header_size = struct.unpack_from("<I", data, 4)[0]
json_size = struct.unpack_from("<I", data, 12)[0]
tree = json.loads(data[16:16 + json_size])
excerpts = []

def visit(node, prefix=""):
    for name, entry in node.get("files", {}).items():
        path = prefix + "/" + name
        if "files" in entry:
            visit(entry, path)
        elif name.endswith((".js", ".cjs")) and "offset" in entry:
            start = 8 + header_size + int(entry["offset"])
            source = data[start:start + entry["size"]].decode(errors="replace")
            for term in ("native-pipe-peer-authorizer", "dynamic_app_tools_peer_authorization_failed", "authorizeNativePipePeer", "native pipe peer authorization"):
                for match in list(re.finditer(re.escape(term), source))[:5]:
                    excerpts.append({"file": path, "term": term, "source": source[max(0, match.start()-4500):match.end()+4500]})

visit(tree)
output.write_text(json.dumps(excerpts, indent=2))
print("Saved", len(excerpts), "shipping transport excerpts.")
