"""Run official device authorization on an ephemeral GitHub runner."""

import json
import os
import pathlib
import re
import subprocess
import sys
import time


if os.environ.get("GITHUB_ACTIONS") != "true":
    raise RuntimeError("Device login belongs to the disposable cloud test host.")

mode = sys.argv[1]
root = pathlib.Path(os.environ["RUNNER_TEMP"]) / "sorter-mcp-acceptance"
codex_home = root / "codex-home"
codex_home.mkdir(parents=True, exist_ok=True)

if mode == "start":
    cli = pathlib.Path(sys.argv[2])
    (codex_home / "config.toml").write_text('cli_auth_credentials_store = "file"\n')
    environment = os.environ.copy()
    environment["CODEX_HOME"] = str(codex_home)
    with (root / "device-login.log").open("w") as log:
        process = subprocess.Popen([str(cli), "login", "--device-auth"], env=environment,
                                   stdin=subprocess.DEVNULL, stdout=log, stderr=log,
                                   start_new_session=True)
    (root / "device-login-pid").write_text(str(process.pid))
    public = pathlib.Path("artifacts/login/public-login.json")
    public.parent.mkdir(parents=True, exist_ok=True)
    for _ in range(90):
        log_text = (root / "device-login.log").read_text(errors="replace")
        clean = re.sub(r"\x1b\[[0-?]*[ -/]*[@-~]", "", log_text)
        url = re.search(r"https://auth\.openai\.com/[^\s]+", clean)
        code = re.search(r"\b[A-Z0-9]{4,8}-[A-Z0-9]{4,8}\b", clean)
        if url and code:
            info = {"verificationUrl": url.group().rstrip(".,)"), "userCode": code.group(),
                    "issuedAt": time.time(), "cloudRun": os.environ["GITHUB_RUN_ID"],
                    "purpose": "Independent Mac desktop sidebar sorter acceptance"}
            public.write_text(json.dumps(info, indent=2))
            print("The one-time device authorization details are available in the public-login artifact.")
            break
        if process.poll() is not None:
            raise RuntimeError("The official device login exited before issuing a code. " + clean[-2000:])
        time.sleep(1)
    else:
        process.terminate()
        raise TimeoutError("The official login did not issue a device code within 90 seconds.")
elif mode == "wait":
    for _ in range(900):
        auth = codex_home / "auth.json"
        if auth.exists():
            value = json.loads(auth.read_text())
            if value.get("tokens"):
                print("The official CLI saved cloud-only login credentials. Continuing acceptance.")
                break
        log_text = (root / "device-login.log").read_text(errors="replace")
        if "Successfully logged in" in log_text and not auth.exists():
            raise RuntimeError("The official CLI completed login without the configured credentials file.")
        if re.search(r"device code.*expired|authorization.*expired|login failed", log_text, re.I):
            raise RuntimeError("The official device authorization expired or failed.")
        time.sleep(1)
    else:
        raise TimeoutError("The cloud device authorization was not completed within 15 minutes.")
else:
    raise ValueError("Choose start or wait.")

