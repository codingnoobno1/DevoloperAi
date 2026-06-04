import 'dart:io';
import '../utils/logger.dart';

class IntegrityChecker {
  static void verify(String dir) {
    Logger.info("📦 Phase: File Integrity Check");
    final directory = Directory(dir);
    if (!directory.existsSync()) return;

    final files = directory.listSync(recursive: true).whereType<File>().where((f) => f.path.endsWith('.dart'));
    int checked = 0;
    int errors = 0;

    for (var file in files) {
      final content = file.readAsStringSync();
      final lines = content.split('\n');
      final currentDir = file.parent.path;

      for (var line in lines) {
        if (line.trim().startsWith('import ') && line.contains("'") && !line.contains('package:')) {
          final importPath = line.split("'")[1];
          if (importPath.endsWith('.dart')) {
            final targetPath = _resolvePath(currentDir, importPath);
            if (!File(targetPath).existsSync()) {
              Logger.error("❌ Integrity Error: File '${file.path}' imports missing file '$importPath'");
              Logger.error("   Target expected at: $targetPath");
              errors++;
            }
          }
        }
      }
      checked++;
    }

    if (errors > 0) {
      throw Exception("❌ File Integrity Check failed with $errors errors. Generation is inconsistent.");
    }
    Logger.success("✅ File Integrity Check passed ($checked files verified).");
  }

  static String _resolvePath(String currentDir, String relativePath) {
    // Convert to absolute or clean path
    final fileDir = Directory(currentDir).absolute.path;
    final normalizedRel = relativePath.replaceAll('/', Platform.pathSeparator);
    
    // Simple join and normalization
    final absolutePath = File(fileDir + Platform.pathSeparator + normalizedRel).absolute.path;
    return absolutePath;
  }
}
