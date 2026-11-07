OutpostLoop Resource Server

This package is a standalone Windows LAN resource server for YooAsset Host files.
It does not require Unity, project source code, upload APIs, HTTPS, public internet
deployment, directory browsing, or live switching while the server is running.

Start:
1. Copy the whole ResourceServerBundle_* directory to the server PC.
2. Double-click start_resource_server.bat.
3. Allow inbound TCP 8000 in Windows Firewall when prompted.

Default port: 8000

After startup, the window prints copyable client resource root URLs:
- Local client: http://127.0.0.1:<port> (only for a client on the server PC).
- LAN clients: http://<server-ip>:<port> (phone or another PC on the same LAN).
If multiple network adapters are active, choose the address on the client's LAN.
Copy the root URL into Settings > Network > Resource Server, or resourceServer
in hostConfig.json. Do not append OutpostLoop.version or another filename.
The printed Version URL is for checking access, not for the client setting.

Client hostConfig.json example:
{
  "resourceServer": "http://192.168.1.20:8000"
}

Validation:
- Open http://<server-ip>:8000/OutpostLoop.version from another LAN device.
- Open http://<server-ip>:8000/resource-update-notices.json to validate the
  standalone update notice route. This route is the only public notice URL.
- GET and HEAD should both return 200 for resource files.
- GET and HEAD should both return 200 for resource-update-notices.json when
  Notices/resource-update-notices.json exists. HEAD returns Content-Length and
  no body. Notice responses include Cache-Control: no-store, no-cache,
  must-revalidate, Pragma: no-cache, and Expires: 0.
- Bundle downloads should match the source file size and SHA-256.
- Requests are limited to GET and HEAD. Other methods return 405 with
  Allow: GET, HEAD.
- The request line and all request headers share a 16 KiB limit. Over-limit,
  malformed, incomplete, or LF-only request headers return 400 Bad Request when
  the connection can still be written.
- Successful responses include Content-Length, Content-Type, and
  Connection: close. HEAD returns the same headers as GET with an empty body.
- Content types:
  .version and .txt: text/plain; charset=utf-8
  .json: application/json; charset=utf-8
  .xml: application/xml; charset=utf-8
  .bytes, .hash, .bundle, and files without an extension:
  application/octet-stream

Update:
1. Stop the server first with Ctrl+C or by closing the window.
2. Replace the whole ResourceFiles directory with the new published ResourceFiles.
3. Start start_resource_server.bat again.

Resource update notices:
1. Edit only Notices/resource-update-notices.json in a separate working copy.
2. Run validate_resource_update_notices.bat before replacing the live file.
3. Replace the single JSON file atomically when possible. The server reopens the
   notice file for every request, so no restart is required for notice changes.
4. Do not expose the Notices directory directly. The server only maps
   /resource-update-notices.json and keeps all other package-root files private.

Rollback:
1. Stop the server.
2. Restore a previously published complete ResourceServerBundle_* package.
3. Start start_resource_server.bat again.

This first version is read-only and LAN-only. It does not provide uploads,
directory listing, remote shutdown, account systems, HTTPS, CDN publishing, or
in-place replacement while the server is running.

Optional Android APK updates:
The server also serves /app-update.json and /app-updates/android/<filename>.apk
from the separate AppUpdates/Android directory beside ResourceFiles. Updating
ResourceFiles does not delete APKs. Missing app-update.json returns 404; clients
continue their normal resource update. Manifest responses disable caching.

The Android menus 完整 Development APK, Host 基础包 and 联网首包
automatically publish the validated APK and app-update.json into the latest
Android resource server bundle. Their success dialogs show the publication path.
No separate publication command is needed for these menus.
Complete APK publication preserves existing ResourceFiles; if no Android server
bundle exists, it creates one first. 联网首包 still publishes matching remote
resources. Host 代码+资源更新 keeps its existing resource-only update behavior.
The built-in complete APK retains its offline startup behavior.
The APK is retained if resource or APK publication fails after APK validation.

For an APK built outside those menus, run in PowerShell:
  .\publish_app_update.ps1 -Apk 'D:\Packages\OutpostLoop-0.1.12.apk' -VersionName '0.1.12' -VersionCode 2
Run this script inside the published resource server bundle, or pass -Bundle
with that bundle's directory. VersionName and VersionCode MUST match the APK's
Android metadata. Keep the same application id and signing key. This script
does not build or sign APKs; the Android installer validates the actual package.

Publication computes size and SHA-256, copies an immutable versioned APK, then
atomically replaces app-update.json. Older APKs remain available for active
downloads. Do not overwrite an existing version with different APK bytes.
No server restart is needed to publish an APK update. An older server script
must first be upgraded and restarted to gain these routes.

Players can download inside the game and choose Install update or Later.
Android may ask them to permit installs from this game. Installation requires
system confirmation. Deploy the first APK containing this feature manually;
existing Boot/native code cannot gain it through Game.dll resource updates.
Old clients that choose Later still need compatible resource updates.

Developer validation from the repository template (uses temporary files and a
temporary HTTP server):
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test_app_updates.ps1
