import 'dart:convert';
import 'dart:io';

/// ModeManager: Reads and writes the project's mode ownership contract.
/// Enforces the rule that Pro Mode prevents ExpansionEngine from overwriting custom_config/.
class ModeManager {
  static const String _systemPath = 'tool/config/system.json';

  static String _modeFilePath() {
    final sys = File(_systemPath);
    if (!sys.existsSync()) return 'config/mode.json';
    final data = jsonDecode(sys.readAsStringSync());
    return data['paths']?['mode_file'] ?? 'config/mode.json';
  }

  static Map<String, dynamic> _read() {
    final file = File(_modeFilePath());
    if (!file.existsSync()) return {"mode": "noob", "locked": false};
    return Map<String, dynamic>.from(jsonDecode(file.readAsStringSync()));
  }

  static void _write(Map<String, dynamic> data) {
    final file = File(_modeFilePath());
    file.parent.createSync(recursive: true);
    file.writeAsStringSync(JsonEncoder.withIndent('  ').convert(data));
  }

  /// Returns true if the project is in Noob Mode (mainapp.json driven).
  static bool isNoob() => _read()['mode'] == 'noob';

  /// Returns true if the project is in Pro Mode (custom_config/ is primary).
  static bool isPro() => _read()['mode'] == 'pro';

  /// Returns true if the mode is locked (prevents switching).
  static bool isLocked() => _read()['locked'] == true;

  /// Returns the current mode string.
  static String currentMode() => _read()['mode'] ?? 'noob';

  /// Sets the project mode. Throws if locked.
  static void setMode(String mode, {bool lock = false}) {
    if (isLocked()) {
      throw Exception("❌ Mode is locked. Edit config/mode.json manually to unlock.");
    }
    if (mode != 'noob' && mode != 'pro') {
      throw Exception("❌ Invalid mode '$mode'. Use 'noob' or 'pro'.");
    }
    final data = _read();
    data['mode'] = mode;
    data['locked'] = lock;
    _write(data);
    print("✅ Mode set to '$mode'${lock ? ' (locked)' : ''}.");
  }

  /// Prints a human-readable status of the current mode.
  static void printStatus() {
    final data = _read();
    final mode = data['mode'] ?? 'noob';
    final locked = data['locked'] == true;
    print("  Mode:     $mode ${locked ? '🔒 (locked)' : '🔓 (unlocked)'}");
    print("  Contract: ${_modeFilePath()}");
    print("  Rule:     ${mode == 'noob' ? 'mainapp.json → ExpansionEngine → custom_config/ → IR' : 'custom_config/ → IR (expansion skipped)'}");
  }
}
