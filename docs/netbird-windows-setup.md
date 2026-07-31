# User Guide — Getting Started (Windows)

The Windows port of the NetBird VPN setup. NetBird is how you reach Sarvam's internal
network (including the kivi service). This mirrors the macOS guide, translated to Windows.

> **Two Windows-isms up front**
> - **There is no `sudo` on Windows.** Anywhere the macOS guide uses `sudo`, run the command
>   from an **elevated terminal** instead: Start → type *PowerShell* → right-click →
>   **Run as administrator**. The `netbird service …` commands require this.
> - **Architecture:** almost every Windows PC (Intel *or* AMD) is **x64**, so use the
>   `amd64` download assets below. Only on a Windows-on-ARM PC (Qualcomm Snapdragon /
>   Copilot+ / Surface Pro X) swap `amd64` → `arm64`.

All commands below are **PowerShell**.

---

## Step 1: Install NetBird (Windows app + CLI)

Pick **one** of the three options. Option A (the official installer) is recommended — it
sets up the CLI, the background service, and the system-tray app in one go.

### Option A — Official installer (recommended)

Run in **PowerShell (Administrator)**:

```powershell
# === DOWNLOAD 0.67.4 installer ===
$ver = "0.67.4"
curl.exe -L -o "$env:TEMP\netbird-installer.exe" `
  "https://github.com/netbirdio/netbird/releases/download/v$ver/netbird_installer_${ver}_windows_amd64.exe"

# === RUN INSTALLER (installs CLI + service + tray app) ===
Start-Process -FilePath "$env:TEMP\netbird-installer.exe" -Wait

# === VERIFY (open a NEW terminal first so PATH refreshes) ===
netbird version
```

### Option B — winget (quickest one-liner)

```powershell
winget install NetBird.NetBird
```

### Option C — Manual (zip + PATH) — the direct analog of the macOS `tar` steps

Run in **PowerShell (Administrator)**:

```powershell
$ver = "0.67.4"
$dir = "$env:ProgramFiles\NetBird"

# download the CLI zip (analog of the darwin tarball)
curl.exe -L -o "$env:TEMP\netbird.zip" `
  "https://github.com/netbirdio/netbird/releases/download/v$ver/netbird_${ver}_windows_amd64.zip"

# extract into Program Files (analog of untarring into /usr/local/bin)
New-Item -ItemType Directory -Force -Path $dir | Out-Null
Expand-Archive -Path "$env:TEMP\netbird.zip" -DestinationPath $dir -Force

# put netbird.exe on the machine PATH (needs admin; analog of /usr/local/bin being on PATH)
[Environment]::SetEnvironmentVariable(
  "Path", [Environment]::GetEnvironmentVariable("Path", "Machine") + ";$dir", "Machine")

# verify (open a NEW terminal so PATH refreshes)
netbird version
```

### Start the service (after any option)

Run in **PowerShell (Administrator)** — this is the translation of the `sudo netbird …`
lines:

```powershell
netbird service install
netbird service start
netbird up --management-url https://vpn.sarvam.ai
```

---

## Step 2: Create your account

1. Open <https://vpn.sarvam.ai> in your browser.
2. Click **Login with Google** using your `@sarvam.ai` account.
3. Your request will be **pending approval** — post the request in **#req-access** on Slack
   for fast approval.

---

## Step 3: Connect

Once approved, connect from your terminal (a normal, non-admin PowerShell is fine once the
service is installed):

```powershell
netbird up --management-url https://vpn.sarvam.ai
```

You'll be prompted to authenticate via browser (Google SSO). After that, you're connected.

### Handy follow-ups

```powershell
netbird status      # show connection state + peers
netbird down        # disconnect
netbird up          # reconnect (management URL is remembered after the first `up`)
```

The installed **NetBird service + tray app** keep the tunnel up across reboots; you only
re-run `netbird up` if you've run `netbird down` or need to re-authenticate.
