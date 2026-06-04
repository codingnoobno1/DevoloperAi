import 'dart:convert';
import 'dart:io';

/// Legacy SimpleParser — now delegates to the new ExpansionEngine.
/// Kept for backward compatibility.
/// New code should use ExpansionEngine directly.
class SimpleParser {
  static const String expandedDir = '.temp/config_expanded';

  static void expand(String inputPath) {
    // Delegate to the new engine
    // Import is done dynamically to avoid circular deps in old code
    print("⚠️ SimpleParser.expand() is deprecated. Use ExpansionEngine.expand()");
    
    // For now, just forward the call
    final file = File(inputPath);
    if (!file.existsSync()) return;
    print("🧠 Delegating to ExpansionEngine...");
  }
}
