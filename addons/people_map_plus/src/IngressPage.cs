namespace PeopleMapPlus.Addon;

public static class IngressPage
{
    public static string Html { get; } = """
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>People Map Plus Add-on</title>
  <style>
    body { font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif; margin: 24px; background: #111; color: #f3f3f3; }
    .card { background: #1b1b1b; border: 1px solid #333; border-radius: 12px; padding: 16px; margin-bottom: 16px; }
    h1 { font-size: 22px; margin: 0 0 16px 0; }
    h2 { font-size: 16px; margin-top: 0; }
    button { background: #1881ff; color: #fff; border: 0; border-radius: 8px; padding: 10px 14px; font-weight: 600; cursor: pointer; margin-right: 8px; }
    button.secondary { background: #2d2d2d; }
    button:disabled { opacity: 0.6; cursor: default; }
    pre { background: #0c0c0c; border: 1px solid #2f2f2f; border-radius: 8px; padding: 10px; overflow: auto; white-space: pre-wrap; }
    .row { margin: 8px 0; }
    .muted { color: #b2b2b2; }
  </style>
</head>
<body>
  <h1>People Map Plus Backend</h1>

  <div class="card">
    <h2>OneDrive Connect (Device Code)</h2>
    <div class="row">
      <button id="connectBtn">Connect to OneDrive</button>
      <button id="pollBtn" class="secondary">Check Authorization</button>
      <button id="syncBtn" class="secondary">Run Sync Now</button>
    </div>
    <div class="row muted">Use Connect once, complete Microsoft login, then Check Authorization.</div>
    <pre id="connectOut">Ready.</pre>
  </div>

  <div class="card">
    <h2>Browse OneDrive Folders</h2>
    <div class="row">
      <label for="folderPath">Path</label>
    </div>
    <div class="row">
      <input id="folderPath" type="text" value="/" style="width: 100%; padding: 8px; border-radius: 8px; border: 1px solid #444; background: #0c0c0c; color: #f3f3f3;" />
    </div>
    <div class="row">
      <button id="foldersBtn" class="secondary">List Folders</button>
    </div>
    <pre id="foldersOut">Use path "/" to list root folders.</pre>
  </div>

  <div class="card">
    <h2>Status</h2>
    <button id="refreshBtn" class="secondary">Refresh Status</button>
    <pre id="statusOut">Loading...</pre>
  </div>

  <script>
    const connectOut = document.getElementById("connectOut");
    const foldersOut = document.getElementById("foldersOut");
    const statusOut = document.getElementById("statusOut");

    async function call(url, method = "GET") {
      const res = await fetch(url, { method, headers: { "content-type": "application/json" } });
      let data = null;
      try { data = await res.json(); } catch (_) {}
      return { ok: res.ok, status: res.status, data };
    }

    function show(el, title, data) {
      el.textContent = title + "\n\n" + JSON.stringify(data, null, 2);
    }

    async function refreshStatus() {
      const [sync, auth] = await Promise.all([
        call("./api/people_map_plus/sync/status"),
        call("./api/people_map_plus/onedrive/device/status")
      ]);
      show(statusOut, "Sync + Auth status", { sync, auth });
    }

    document.getElementById("connectBtn").onclick = async () => {
      const r = await call("./api/people_map_plus/onedrive/device/start", "POST");
      show(connectOut, "Connect result", r);
      if (r.data && r.data.verificationUri && r.data.userCode) {
        connectOut.textContent += "\n\nOpen: " + r.data.verificationUri + "\nCode: " + r.data.userCode;
      }
      await refreshStatus();
    };

    document.getElementById("pollBtn").onclick = async () => {
      const r = await call("./api/people_map_plus/onedrive/device/poll", "POST");
      show(connectOut, "Poll result", r);
      await refreshStatus();
    };

    document.getElementById("syncBtn").onclick = async () => {
      const r = await call("./api/people_map_plus/sync/run", "POST");
      show(connectOut, "Sync run result", r);
      await refreshStatus();
    };

    document.getElementById("foldersBtn").onclick = async () => {
      const path = (document.getElementById("folderPath").value || "/").trim();
      const r = await call("./api/people_map_plus/onedrive/folders?path=" + encodeURIComponent(path));
      show(foldersOut, "Folders result", r);
    };

    document.getElementById("refreshBtn").onclick = refreshStatus;
    refreshStatus();
  </script>
</body>
</html>
""";
}
