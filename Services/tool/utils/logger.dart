class Logger {
  static bool debugEnabled = false;

  static void info(String msg) => print("⚡ [INFO] $msg");
  static void success(String msg) => print("✅ $msg");
  static void error(String msg, [dynamic error]) {
    print("❌ [ERROR] $msg");
    if (error != null) print("   $error");
  }
  static void warn(String msg) => print("⚠️ [WARN] $msg");
  static void debug(String msg) {
    if (debugEnabled) print("🐞 [DEBUG] $msg");
  }
  static void phase(String name) => print("\n📦 Phase: $name");
}
