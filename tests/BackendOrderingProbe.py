import json
import os
import pathlib
import queue
import subprocess
import sys
import tempfile
import threading
import time
import uuid

if os.environ.get("GITHUB_ACTIONS") != "true":
    raise RuntimeError("Use a disposable cloud host for backend ordering probes.")

cli = pathlib.Path(sys.argv[1]).resolve()
output = pathlib.Path(sys.argv[2]).resolve()
output.parent.mkdir(parents=True, exist_ok=True)
root = pathlib.Path(tempfile.mkdtemp(prefix="sorter-backend-", dir=os.environ["RUNNER_TEMP"]))
environment = os.environ.copy()
environment["CODEX_HOME"] = str(root)
report = {
    "dataKind": "isolated fictional backend projects and chats",
    "desktopSidebarSyncVerified": False,
    "backendOrderingPassed": False,
    "requests": [],
}
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
    started = time.monotonic()
    record = {"method": method}
    report["requests"].append(record)
    process.stdin.write(json.dumps({"id": request_id, "method": method, "params": parameters}) + "\n")
    process.stdin.flush()
    deadline = started + 30
    while True:
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            raise TimeoutError("The backend did not finish " + method)
        try:
            message = messages.get(timeout=remaining)
        except queue.Empty:
            raise TimeoutError("The backend did not finish " + method) from None
        if message is None:
            raise RuntimeError("The backend closed before responding to " + method)
        if message.get("id") != request_id:
            continue
        record["elapsedMilliseconds"] = round((time.monotonic() - started) * 1000, 2)
        if "error" in message:
            record["error"] = message["error"]
            raise RuntimeError(method + " returned " + json.dumps(message["error"]))
        record["success"] = True
        return message["result"]


try:
    report["cliVersion"] = subprocess.check_output([str(cli), "--version"], text=True).strip()
    with (output.parent / "backend-stderr.log").open("w") as errors:
        process = subprocess.Popen([str(cli), "app-server"], env=environment,
                                   stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=errors,
                                   text=True, bufsize=1)
        threading.Thread(target=read_messages, daemon=True).start()
        request("initialize", {"clientInfo": {"name": "sidebar-sorter-cloud", "version": "1.4.0"},
                               "capabilities": {"experimentalApi": True}})
        process.stdin.write(json.dumps({"method": "initialized"}) + "\n")
        process.stdin.flush()
        projects = []
        for name in ("Zulu", "Alpha"):
            directory = root / name
            directory.mkdir()
            value = request("project/create", {"name": name, "roots": [{"path": str(directory)}],
                                                "idempotencyKey": str(uuid.uuid4())})
            projects.append(value["project"])
        before = request("project/list", {"limit": 50, "sortKey": "position", "sortDirection": "asc"})
        report["projectOrderBefore"] = [project["name"] for project in before["data"]]
        request("project/move", {"projectId": projects[1]["id"], "beforeProjectId": projects[0]["id"]})
        after = request("project/list", {"limit": 50, "sortKey": "position", "sortDirection": "asc"})
        report["projectOrderAfter"] = [project["name"] for project in after["data"]]
        if report["projectOrderAfter"] != ["Alpha", "Zulu"]:
            raise RuntimeError("The official backend did not persist the project order.")
        report["projectMovePassed"] = True
        threads = []
        for name in ("Zulu chat", "Alpha chat"):
            value = request("thread/start", {"cwd": projects[0]["roots"][0]["path"], "ephemeral": False,
                                               "approvalPolicy": "never", "sandbox": "read-only"})
            thread = value["thread"]
            request("thread/name/set", {"threadId": thread["id"], "name": name})
            request("thread/metadata/update", {"threadId": thread["id"], "projectId": projects[0]["id"]})
            threads.append(thread)
        request("thread/section/move", {"threadId": threads[0]["id"], "sectionId": None})
        request("thread/section/move", {"threadId": threads[1]["id"], "sectionId": None,
                                        "beforeThreadId": threads[0]["id"]})
        ordered = request("thread/list", {"projectId": projects[0]["id"], "sortKey": "section_position",
                                          "sortDirection": "asc", "useStateDbOnly": True, "limit": 50})
        report["threadIdsAfter"] = [thread["id"] for thread in ordered["data"]]
        report["threadOrderPassed"] = report["threadIdsAfter"] == [threads[1]["id"], threads[0]["id"]]
        if not report["threadOrderPassed"]:
            raise RuntimeError("The section ordering API did not produce the requested project-chat order.")
        report["backendOrderingPassed"] = True
except Exception as error:
    report["error"] = str(error)
finally:
    if process is not None:
        process.stdin.close()
        try:
            process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            process.terminate()
            process.wait(timeout=5)
        report["backendExitCode"] = process.returncode
    output.write_text(json.dumps(report, indent=2))
    print(json.dumps(report))

sys.exit(0 if report["backendOrderingPassed"] else 1)
