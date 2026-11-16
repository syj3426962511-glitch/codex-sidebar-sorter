"""Create fictional sidebar projects through the official cloud-local app server."""

import json
import os
import pathlib
import queue
import subprocess
import sys
import threading
import uuid

if os.environ.get("GITHUB_ACTIONS") != "true" or sys.platform != "darwin":
    raise RuntimeError("Use the disposable cloud Mac.")

root = pathlib.Path(os.environ["RUNNER_TEMP"]) / "sorter-mcp-acceptance"
receipt = root / "ax-fixture.json"
if receipt.exists():
    print("Reusing the existing fictional sidebar fixture.")
    sys.exit(0)

environment = os.environ.copy()
environment["CODEX_HOME"] = str(root / "codex-home")
messages = queue.Queue()
process = None
request_id = 0


def read_messages():
    for line in process.stdout:
        try:
            messages.put(json.loads(line))
        except json.JSONDecodeError:
            pass
    messages.put(None)


def request(method, parameters):
    global request_id
    request_id += 1
    process.stdin.write(json.dumps({"id": request_id, "method": method, "params": parameters}) + "\n")
    process.stdin.flush()
    while True:
        message = messages.get(timeout=30)
        if message is None:
            raise RuntimeError("The fixture backend closed unexpectedly.")
        if message.get("id") != request_id:
            continue
        if "error" in message:
            raise RuntimeError(method + " failed: " + json.dumps(message["error"]))
        return message["result"]


try:
    with (root / "ax-fixture-stderr.log").open("w") as errors:
        process = subprocess.Popen([sys.argv[1], "app-server"], env=environment,
                                   stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=errors,
                                   text=True, bufsize=1)
        threading.Thread(target=read_messages, daemon=True).start()
        request("initialize", {"clientInfo": {"name": "sidebar-sorter-ax-cloud", "version": "1.4.1"},
                               "capabilities": {"experimentalApi": True}})
        process.stdin.write(json.dumps({"method": "initialized"}) + "\n")
        process.stdin.flush()
        projects = []
        for title in ("Zulu AX fixture", "Alpha AX fixture"):
            directory = root / "fixture-projects" / title
            directory.mkdir(parents=True, exist_ok=True)
            project = request("project/create", {"name": title, "roots": [{"path": str(directory)}],
                                                 "idempotencyKey": str(uuid.uuid4())})["project"]
            project["fixtureChats"] = []
            for name in ("Zulu fixture chat", "Middle fixture chat", "Alpha fixture chat"):
                thread = request("thread/start", {"cwd": str(directory), "ephemeral": False,
                                                   "approvalPolicy": "never", "sandbox": "read-only"})["thread"]
                request("thread/name/set", {"threadId": thread["id"], "name": name})
                request("thread/metadata/update", {"threadId": thread["id"], "projectId": project["id"]})
                project["fixtureChats"].append({"id": thread["id"], "name": name})
            projects.append(project)
        receipt.write_text(json.dumps({"dataKind": "fictional cloud-local AX projects", "projects": projects}, indent=2))
        print("Created two fictional projects and six chats in the disposable cloud home.")
finally:
    if process is not None:
        process.stdin.close()
        try:
            process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            process.terminate()
            process.wait(timeout=5)
