package com.godspace.outpostloop.restart;

import android.app.Activity;
import android.app.ActivityManager;
import android.content.Intent;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.os.Process;
import android.util.Log;
import android.widget.TextView;
import java.util.List;

/** 独立进程负责一次冷启动交接；不加载 Unity、不下载资源、不修改应用数据。 */
public final class ResourceRestartActivity extends Activity {
    private final Handler handler = new Handler(Looper.getMainLooper());
    private int mainPid;
    private int attempts;
    private String failureText;

    /** 校验主进程和启动入口后才结束旧进程；无效请求不会关闭游戏。 */
    @Override protected void onCreate(Bundle state) {
        super.onCreate(state);
        failureText = getIntent().getStringExtra("failure_text");
        mainPid = getIntent().getIntExtra("main_pid", -1);
        if (mainPid <= 0 || mainPid == Process.myPid() || launchIntent() == null) {
            showFailure();
            return;
        }
        try {
            Process.killProcess(mainPid);
            waitForOldProcess();
        } catch (RuntimeException exception) {
            Log.e("OutpostLoopRestart", "Restart handoff failed", exception);
            showFailure();
        }
    }

    private Intent launchIntent() {
        Intent intent = getPackageManager().getLaunchIntentForPackage(getPackageName());
        if (intent != null) intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_CLEAR_TASK);
        return intent;
    }

    /** 等旧 Unity 进程退出后再拉起，避免启动意图仍被旧实例接收。 */
    private void waitForOldProcess() {
        ActivityManager manager = (ActivityManager) getSystemService(ACTIVITY_SERVICE);
        List<ActivityManager.RunningAppProcessInfo> processes = manager.getRunningAppProcesses();
        if (processes != null) {
            for (ActivityManager.RunningAppProcessInfo process : processes) {
                if (process.pid == mainPid) {
                    if (++attempts >= 40) { showFailure(); return; }
                    handler.postDelayed(this::waitForOldProcess, 50);
                    return;
                }
            }
        }
        try {
            Intent intent = launchIntent();
            if (intent == null) { showFailure(); return; }
            startActivity(intent);
            finish();
            Process.killProcess(Process.myPid());
        } catch (RuntimeException exception) {
            Log.e("OutpostLoopRestart", "Restart launch failed", exception);
            showFailure();
        }
    }

    /** 主进程已经结束却无法拉起时，保留可读提示供玩家手动处理。 */
    private void showFailure() {
        TextView text = new TextView(this);
        text.setText(failureText == null ? getPackageName() : failureText);
        text.setTextSize(20);
        text.setGravity(android.view.Gravity.CENTER);
        setContentView(text);
    }

    @Override protected void onDestroy() {
        handler.removeCallbacksAndMessages(null);
        super.onDestroy();
    }
}
