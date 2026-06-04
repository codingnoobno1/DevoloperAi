import 'dart:convert';
import 'dart:io';
import 'package:crypto/crypto.dart';
import 'logger.dart';

/// CacheManager v2.0: Advanced Project State Invalidation.
/// Tracks hashes of inputs, configurations, rules, and generator logic.
class CacheManager {
  static const String cachePath = '.thunder/cache.json';

  /// Critical files that impact the compilation result.
  static const List<String> criticalPaths = [
    'tool/config/system.json',
    'tool/config/expansion_rules.json',
    'tool/config/templates.json',
    'tool/config/naming_rules.json',
    'tool/config/navigation_rules.json',
    'tool/config/themes/default_theme.json',
  ];

  /// Checks if the project state is identical to the last successful build.
  /// Also verifies that the output directory exists and is not empty.
  static Future<bool> isUpToDate({
    String? mainInputPath, 
    String? outputDir,
    Map<String, dynamic>? env
  }) async {
    final currentHash = _calculateProjectHash(mainInputPath, env);
    final cacheFile = File(cachePath);
    
    if (!cacheFile.existsSync()) return false;

    // Output check: if output directory is missing or empty, it's NOT up to date.
    if (outputDir != null) {
      final dir = Directory(outputDir);
      if (!dir.existsSync() || dir.listSync().isEmpty) {
        Logger.debug("Output directory '$outputDir' is missing or empty. Forcing generation.");
        return false;
      }
    }

    try {
      final cache = jsonDecode(cacheFile.readAsStringSync());
      final isMatch = cache['hash'] == currentHash;
      if (isMatch) {
        Logger.info("Project state is identical. HASH: ${currentHash.substring(0, 8)}...");
      }
      return isMatch;
    } catch (_) {
      return false;
    }
  }

  /// Updates the cache with the current project state hash.
  static void updateCache({String? mainInputPath, Map<String, dynamic>? env}) {
    final hash = _calculateProjectHash(mainInputPath, env);
    final cacheFile = File(cachePath);
    
    if (!cacheFile.parent.existsSync()) cacheFile.parent.createSync(recursive: true);

    cacheFile.writeAsStringSync(jsonEncode({
      "hash": hash,
      "lastBuild": DateTime.now().toIso8601String(),
      "metadata": {
        "input": mainInputPath,
        "env": env,
      }
    }));
  }

  /// Calculates a single SHA-256 hash representing the ENTIRE project state.
  static String _calculateProjectHash(String? mainInputPath, Map<String, dynamic>? env) {
    final buffer = StringBuffer();

    // 1. Hash the main input (mainapp.json) - if in Noob mode
    if (mainInputPath != null) {
      final mainFile = File(mainInputPath);
      if (mainFile.existsSync()) {
        buffer.write(mainFile.readAsStringSync());
      }
    }

    // 2. Hash all files in custom_config/ (the IR layer)
    final systemFile = File('tool/config/system.json');
    final customConfigDir = systemFile.existsSync() 
        ? (jsonDecode(systemFile.readAsStringSync())['paths']?['custom_config'] ?? 'config/custom_config/')
        : 'config/custom_config/';
    
    final dir = Directory(customConfigDir);
    if (dir.existsSync()) {
      final files = dir.listSync().whereType<File>().toList();
      files.sort((a, b) => a.path.compareTo(b.path));
      for (var file in files) {
        buffer.write(file.readAsStringSync());
      }
    }

    // 3. Hash all critical configuration files (rules, templates, naming)
    for (var path in criticalPaths) {
      final file = File(path);
      if (file.existsSync()) {
        buffer.write(file.readAsStringSync());
      }
    }

    // 4. Hash the environment/flags
    if (env != null) {
      buffer.write(jsonEncode(env));
    }

    // 5. Generator logic fingerprint
    buffer.write(_generateLogicFingerprint());

    return sha256.convert(utf8.encode(buffer.toString())).toString();
  }

  static String _generateLogicFingerprint() {
    final toolDir = Directory('tool');
    if (!toolDir.existsSync()) return "no-tool-dir";

    final files = toolDir.listSync(recursive: true).whereType<File>().toList();
    files.sort((a, b) => a.path.compareTo(b.path));

    final buffer = StringBuffer();
    for (var file in files) {
      // Include path and last modified to detect changes without full content hashing (fast)
      buffer.write(file.path);
      buffer.write(file.lastModifiedSync().millisecondsSinceEpoch);
    }
    return sha256.convert(utf8.encode(buffer.toString())).toString();
  }
}
