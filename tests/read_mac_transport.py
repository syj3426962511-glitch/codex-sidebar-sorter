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
terms = ("native-pipe-peer-authorizer", "dynamic_app_tools_peer_authorization_failed", "authorizeNativePipePeer", "native pipe peer authorization")
if "--ordering" in sys.argv[3:]:
    terms = ("sidebar-project-thread-orders", "thread/section/move", "project/move", "project/list", "section_position")
if "--mcp" in sys.argv[3:]:
    terms = ("mcpServerStatus/list", "sendMessageFromView", "app-server-request", "electronBridge", "native-pipe-peer-authorizer", "untrusted-process-ancestry")

def visit(node, prefix=""):
    for name, entry in node.get("files", {}).items():
        path = prefix + "/" + name
        if "files" in entry:
            visit(entry, path)
        elif name.endswith((".js", ".cjs")) and "offset" in entry:
            start = 8 + header_size + int(entry["offset"])
            source = data[start:start + entry["size"]].decode(errors="replace")
            for term in terms:
                for match in list(re.finditer(re.escape(term), source))[:3]:
                    excerpts.append({"file": path, "term": term, "source": source[max(0, match.start()-1600):match.end()+1600]})

visit(tree)
output.write_text(json.dumps(excerpts, indent=2))
print("Saved", len(excerpts), "shipping transport excerpts.")
