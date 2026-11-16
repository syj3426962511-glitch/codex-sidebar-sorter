import glob
import json
import os
import pathlib
import select
import struct
import subprocess
import sys
import time

if os.environ.get("GITHUB_ACTIONS") != "true" or sys.platform != "darwin":
    raise RuntimeError("Use the disposable cloud Mac.")

cli, output = sys.argv[1:3]
report = {"transport": "official CLI stdio-to-uds", "success": False}
process = None


def read_exact(stream, size):
    result = bytearray()
    deadline = time.monotonic() + 20
    while len(result) < size:
        remaining = deadline - time.monotonic()
        if remaining <= 0 or not select.select([stream], [], [], remaining)[0]:
            report["receivedBytes"] = len(result)
            report["expectedBytes"] = size
            raise TimeoutError("The native relay did not complete its response within 20 seconds.")
        block = os.read(stream.fileno(), size - len(result))
        if not block:
            break
        result.extend(block)
    return bytes(result)


try:
    sockets = glob.glob("/tmp/codex-app-tools/*.sock")
    if not sockets:
        raise RuntimeError("The official desktop socket is unavailable.")
    process = subprocess.Popen(
        [cli, "stdio-to-uds", sockets[0]], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE
    )
    payload = json.dumps({"id": 1, "jsonrpc": "2.0", "method": "tools/list", "params": {"threadStartKind": "all"}}).encode()
    process.stdin.write(struct.pack("<I", len(payload)) + payload)
    process.stdin.flush()
    prefix = read_exact(process.stdout, 4)
    if len(prefix) != 4:
        raise RuntimeError("The signed relay received no native frame.")
    size = struct.unpack("<I", prefix)[0]
    if size < 1 or size > 8 * 1024 * 1024:
        raise RuntimeError("The native frame size is invalid.")
    report["nativeFrameBytes"] = size
    result = json.loads(read_exact(process.stdout, size))
    report["nativeResponse"] = "error" if "error" in result else "result"
    report["toolCount"] = len(result.get("result", {}).get("tools", []))
    report["success"] = "result" in result
    if "error" in result:
        report["error"] = result["error"]
except Exception as error:
    report["error"] = str(error)
finally:
    if process is not None:
        process.stdin.close()
        try:
            process.wait(timeout=3)
        except subprocess.TimeoutExpired:
            process.terminate()
            process.wait(timeout=3)
    pathlib.Path(output).write_text(json.dumps(report, indent=2))
print(json.dumps(report))
sys.exit(0 if report["success"] else 1)
