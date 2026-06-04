import 'dart:io';
import 'code_sanitizer.dart';

class FileWriter {
  /// Whether to print each file being written (debug mode)
  static bool debugMode = false;

  static void write(String path, String content) {
    final file = File(path);
    if (!file.parent.existsSync()) {
      file.parent.createSync(recursive: true);
    }
    // Sanitize all generated code before writing
    final sanitized = CodeSanitizer.sanitize(content);
    file.writeAsStringSync(sanitized);

    if (debugMode) {
      print('  ✅ Generated: $path');
    }
  }

  static void clean(String directory) {
    final dir = Directory(directory);
    if (dir.existsSync()) {
      dir.deleteSync(recursive: true);
    }
    dir.createSync(recursive: true);
  }
}
