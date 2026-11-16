"""Cloud-only MCP server used to verify desktop process authorization."""

import glob
import json
import os
import pathlib
import select
import socket
import struct
import subprocess
import sys
import threading
import time


if os.environ.get("GITHUB_ACTIONS") != "true" or sys.platform != "darwin":
    raise RuntimeError("Use a disposable cloud Mac for the MCP ancestry probe.")

output = pathlib.Path(sys.argv[1])
output.mkdir(parents=True, exist_ok=True)
report = {
    "pid": os.getpid(),
    "parentPid": os.getppid(),
    "mcpInitialized": False,
    "nativeCatalogAccessible": False,
}


def save():
    destination = output / ("bridge-" + str(os.getpid()) + ".json")
    temporary = destination.with_suffix(".tmp")
    temporary.write_text(json.dumps(report, indent=2))
    temporary.replace(destination)


def read_exact(stream, size):
    value = bytearray()
    deadline = time.monotonic() + 15
    while len(value) < size:
        remaining = deadline - time.monotonic()
        if remaining <= 0 or not select.select([stream], [], [], remaining)[0]:
            raise TimeoutError("The native transport did not respond within 15 seconds.")
        part = os.read(stream.fileno(), size - len(value))
        if not part:
            raise EOFError("The desktop closed the native connection.")
        value.extend(part)
    return bytes(value)


def check_native():
    ancestry = []
    pid = os.getpid()
    for _ in range(10):
        value = subprocess.check_output(
            ["/bin/ps", "-p", str(pid), "-o", "ppid=", "-o", "comm="], text=True
        ).strip()
        if not value:
            break
        parent, command = value.split(None, 1)
        ancestry.append({"pid": pid, "parentPid": int(parent), "command": command})
        pid = int(parent)
        if pid < 1:
            break
    report["ancestry"] = ancestry
    report["attempts"] = []
    for endpoint in glob.glob("/tmp/codex-app-tools/*.sock"):
        record = {"endpoint": endpoint, "transport": "official signed CLI relay from desktop-owned MCP"}
        report["attempts"].append(record)
        peer = None
        try:
            relay = os.environ["SORTER_CLOUD_RELAY"]
            peer = subprocess.Popen([relay, "stdio-to-uds", endpoint], stdin=subprocess.PIPE,
                                    stdout=subprocess.PIPE, stderr=subprocess.PIPE)
            record["relayPid"] = peer.pid
            payload = json.dumps({
                "jsonrpc": "2.0", "id": 1, "method": "tools/list",
                "params": {"threadStartKind": "all"},
            }).encode()
            peer.stdin.write(struct.pack("<I", len(payload)) + payload)
            peer.stdin.flush()
            length = struct.unpack("<I", read_exact(peer.stdout, 4))[0]
            if length < 1 or length > 8 * 1024 * 1024:
                raise ValueError("The desktop returned an invalid native frame length.")
            response = json.loads(read_exact(peer.stdout, length))
            if "error" in response:
                record["error"] = response["error"]
            else:
                tools = response["result"]["tools"]
                record["toolCount"] = len(tools)
                report["nativeCatalogAccessible"] = True
        except (OSError, ValueError, EOFError, KeyError) as error:
            record["error"] = str(error)
        finally:
            if peer is not None:
                peer.terminate()
                peer.wait(timeout=3)
                record["relayExitCode"] = peer.returncode
                record["relayStderr"] = peer.stderr.read().decode(errors="replace")[-2000:]
    report["checkedAt"] = time.time()
    save()


save()
for line in sys.stdin:
    message = json.loads(line)
    method = message.get("method")
    result = None
    if method == "initialize":
        report["mcpInitialized"] = True
        report["clientInfo"] = message.get("params", {}).get("clientInfo")
        save()
        result = {
            "protocolVersion": message["params"]["protocolVersion"],
            "capabilities": {"tools": {}},
            "serverInfo": {"name": "sidebar-sorter-ancestry-probe", "version": "0.1.0"},
        }
    elif method == "notifications/initialized":
        threading.Thread(target=check_native, daemon=True).start()
    elif method == "tools/list":
        result = {"tools": [{
            "name": "sorter_bridge_status",
            "description": "Read the cloud-only sorting bridge connection status.",
            "inputSchema": {"type": "object", "properties": {}},
            "annotations": {"readOnlyHint": True},
        }]}
    elif method == "tools/call":
        result = {"content": [{"type": "text", "text": json.dumps(report)}]}
    elif method == "ping":
        result = {}
    if "id" in message:
        response = {"jsonrpc": "2.0", "id": message["id"]}
        if result is None:
            response["error"] = {"code": -32601, "message": "Method not found"}
        else:
            response["result"] = result
        print(json.dumps(response), flush=True)

