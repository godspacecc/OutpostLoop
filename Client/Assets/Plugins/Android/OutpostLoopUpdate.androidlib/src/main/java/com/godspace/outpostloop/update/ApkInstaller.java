package com.godspace.outpostloop.update;

import android.app.Activity;
import android.content.ActivityNotFoundException;
import android.content.Intent;
import android.content.pm.PackageInfo;
import android.content.pm.PackageManager;
import android.content.pm.Signature;
import android.net.Uri;
import android.os.Build;
import android.provider.Settings;
import android.util.Log;
import java.io.File;
import java.util.Arrays;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.atomic.AtomicInteger;
import org.json.JSONObject;

/** 验证候选 APK 的包名、版本与签名后，将它交给系统安装器。 */
public final class ApkInstaller {
    private ApkInstaller() { }

    private static final ConcurrentHashMap<String, InstallRequest> requests = new ConcurrentHashMap<>();
    private static final class InstallRequest {
        final AtomicInteger phase = new AtomicInteger(0);
        volatile String response = response(2, "", "validating", "");
    }

    private static String response(int result, String code, String stage, String details) {
        try {
            JSONObject value = new JSONObject();
            value.put("schemaVersion", 1); value.put("result", result);
            value.put("errorCode", code); value.put("stage", stage);
            value.put("details", details == null ? "" : details);
            return value.toString();
        } catch (Exception impossible) { throw new IllegalStateException(impossible); }
    }

    private static void complete(String id, InstallRequest request, int result, String code, String stage, String details) {
        if (request.phase.get() != 2 && requests.get(id) == request)
            request.response = response(result, code, stage, details);
    }

    public static void beginInstall(Activity activity, String path, String versionName, int versionCode, String id) {
        InstallRequest request = new InstallRequest();
        if (requests.putIfAbsent(id, request) != null) throw new IllegalArgumentException("Duplicate install request");
        new Thread(() -> {
            try {
                File root = new File(getUpdateDirectory(activity)).getCanonicalFile();
                File file = new File(path).getCanonicalFile();
                if (!root.equals(file.getParentFile()) || !file.isFile() || !file.getName().matches("[0-9a-f]{64}\\.apk")) {
                    complete(id, request, -1, "INVALID_FILE", "file_validation", "Missing APK or invalid private update path"); return;
                }
                PackageManager manager = activity.getPackageManager();
                int flags = Build.VERSION.SDK_INT >= 28 ? PackageManager.GET_SIGNING_CERTIFICATES : PackageManager.GET_SIGNATURES;
                PackageInfo installed = manager.getPackageInfo(activity.getPackageName(), flags);
                PackageInfo candidate = manager.getPackageArchiveInfo(file.getAbsolutePath(), flags);
                if (candidate == null) {
                    complete(id, request, -1, "APK_UNREADABLE", "package_validation", "PackageManager cannot parse APK archive"); return;
                }
                if (!activity.getPackageName().equals(candidate.packageName)) {
                    complete(id, request, -1, "PACKAGE_MISMATCH", "package_validation", "Installed=" + installed.packageName + "; candidate=" + candidate.packageName); return;
                }
                if (!versionName.equals(candidate.versionName) || candidate.versionCode != versionCode || candidate.versionCode < installed.versionCode) {
                    complete(id, request, -1, "VERSION_MISMATCH", "version_validation", "InstalledCode=" + installed.versionCode + "; candidate=" + candidate.versionName + "/" + candidate.versionCode); return;
                }
                if (!sameSigners(installed, candidate)) {
                    complete(id, request, -1, "SIGNATURE_MISMATCH", "signature_validation", "Installed and candidate APK signing certificates differ"); return;
                }
                final Intent intent;
                final int outcome;
                if (Build.VERSION.SDK_INT >= 26 && !manager.canRequestPackageInstalls()) {
                    intent = new Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:" + activity.getPackageName()));
                    outcome = 1;
                } else {
                    Uri uri = new Uri.Builder().scheme("content").authority(activity.getPackageName() + ".apkupdates").appendPath(file.getName()).build();
                    intent = new Intent(Intent.ACTION_VIEW).setDataAndType(uri, "application/vnd.android.package-archive").addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
                    outcome = 0;
                }
                activity.runOnUiThread(() -> {
                    if (requests.get(id) != request || !request.phase.compareAndSet(0, 1)) return;
                    try {
                        activity.startActivity(intent);
                        complete(id, request, outcome, "", outcome == 1 ? "permission_settings" : "system_installer", "System activity started");
                    } catch (ActivityNotFoundException failure) {
                        complete(id, request, -1, "ACTIVITY_NOT_FOUND", "system_intent", Log.getStackTraceString(failure));
                    } catch (SecurityException failure) {
                        complete(id, request, -1, "SECURITY_EXCEPTION", "system_intent", Log.getStackTraceString(failure));
                    } catch (RuntimeException failure) {
                        complete(id, request, -1, "INTENT_EXCEPTION", "system_intent", Log.getStackTraceString(failure));
                    }
                });
            } catch (Exception failure) {
                Log.e("OutpostLoopUpdate", "APK request failed", failure);
                complete(id, request, -1, "VALIDATION_EXCEPTION", "package_validation", Log.getStackTraceString(failure));
            }
        }, "OutpostLoopApkValidation").start();
    }

    public static String getInstallResult(String id) {
        InstallRequest request = requests.get(id);
        return request == null ? response(-1, "REQUEST_EXPIRED", "bridge", "Install request no longer exists") : request.response;
    }

    public static void cancelInstall(String id) {
        InstallRequest request = requests.get(id);
        if (request == null) return;
        request.phase.set(2);
        requests.remove(id, request);
    }

    public static int getVersionCode(Activity activity) throws Exception {
        return activity.getPackageManager().getPackageInfo(activity.getPackageName(), 0).versionCode;
    }

    public static String getUpdateDirectory(Activity activity) {
        return new File(activity.getFilesDir(), "ApkUpdates").getAbsolutePath();
    }

    private static boolean sameSigners(PackageInfo current, PackageInfo next) {
        Signature[] a = Build.VERSION.SDK_INT >= 28 && current.signingInfo != null
            ? current.signingInfo.getApkContentsSigners() : current.signatures;
        Signature[] b = Build.VERSION.SDK_INT >= 28 && next.signingInfo != null
            ? next.signingInfo.getApkContentsSigners() : next.signatures;
        if (a == null || b == null || a.length == 0 || a.length != b.length) return false;
        for (Signature signature : a) {
            boolean found = false;
            for (Signature other : b) if (Arrays.equals(signature.toByteArray(), other.toByteArray())) found = true;
            if (!found) return false;
        }
        return true;
    }
}
