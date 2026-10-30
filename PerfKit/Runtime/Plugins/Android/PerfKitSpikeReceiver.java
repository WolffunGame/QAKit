package com.qa.perfkit;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.os.Build;

import java.util.concurrent.atomic.AtomicInteger;

/**
 * PerfKit development test hook. `adb shell am broadcast -a com.qa.perfkit.SPIKE --ei ms 80` stores a sleep request;
 * the kit (C#, PerfSpikeReceiver) collects it about once a second with take() and sleeps the Unity main thread once,
 * producing one real dropped frame. Registered dynamically by the kit only in development builds; nothing here runs
 * unless the kit calls register().
 */
public final class PerfKitSpikeReceiver extends BroadcastReceiver {
    public static final String ACTION = "com.qa.perfkit.SPIKE";
    public static final int DEFAULT_MS = 80;

    // Context.RECEIVER_EXPORTED (API 33) as a literal, so games compiling against an older SDK still build.
    private static final int RECEIVER_EXPORTED = 0x2;

    private static final AtomicInteger pendingMs = new AtomicInteger();
    private static PerfKitSpikeReceiver registered;

    /** Registers one receiver on the application context (exported: the sender is adb shell). Idempotent. */
    public static synchronized boolean register(Context context) {
        if (registered != null) return true;
        Context app = context.getApplicationContext() != null ? context.getApplicationContext() : context;
        PerfKitSpikeReceiver receiver = new PerfKitSpikeReceiver();
        IntentFilter filter = new IntentFilter(ACTION);
        if (Build.VERSION.SDK_INT >= 33) {
            app.registerReceiver(receiver, filter, RECEIVER_EXPORTED);
        } else {
            app.registerReceiver(receiver, filter);
        }
        registered = receiver;
        return true;
    }

    /** Returns the pending sleep in ms (0 = none) and clears it. */
    public static int take() {
        return pendingMs.getAndSet(0);
    }

    @Override
    public void onReceive(Context context, Intent intent) {
        if (intent == null || !ACTION.equals(intent.getAction())) return;
        int ms = intent.getIntExtra("ms", DEFAULT_MS);
        if (ms > 0) pendingMs.set(ms);
    }
}
