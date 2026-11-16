"""Prepare Accessibility authorization for a disposable GitHub macOS runner."""

import json
import os
import pathlib
import subprocess
import sys

if os.environ.get("GITHUB_ACTIONS") != "true" or sys.platform != "darwin":
    raise RuntimeError("Use the disposable cloud Mac.")

root = pathlib.Path(os.environ["RUNNER_TEMP"]) / "sorter-ax-acceptance"
root.mkdir(exist_ok=True)
helper = root / "SorterAXProbe"
subprocess.run(["swiftc", "-O", "tests/MacAccessibilityProbe.swift", "-o", str(helper)], check=True)
subprocess.run(["codesign", "--force", "--sign", "-", "--identifier", "org.sidebarsorter.cloud.axprobe", str(helper)], check=True)

# This authorization applies only inside the short-lived test machine.
# The shipped application will use the normal system consent prompt.
database = "/Library/Application Support/com.apple.TCC/TCC.db"
schema = subprocess.check_output(["sudo", "sqlite3", database, ".schema access"], text=True)
escaped = str(helper).replace("'", "''")
columns = "service,client,client_type,auth_value,auth_reason,auth_version,csreq,policy_id,indirect_object_identifier_type,indirect_object_identifier,indirect_object_code_identity,flags,last_modified"
values = f"'kTCCServiceAccessibility','{escaped}',1,2,4,1,NULL,NULL,0,'UNUSED',NULL,0,strftime('%s','now')"
subprocess.run(["sudo", "sqlite3", database,
                f"INSERT OR REPLACE INTO access ({columns}) VALUES ({values});"], check=True)
print(json.dumps({"helper": str(helper), "cloudOnlyAuthorization": True, "schema": schema}))
