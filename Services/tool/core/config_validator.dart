import 'dart:convert';
import 'dart:io';
import '../utils/logger.dart';

/// ConfigValidator v1.0: Schema and Dependency Validator for custom_config/.
/// Validates each JSON file against its expected schema and checks cross-file
/// dependency consistency before the IR is built.
class ConfigValidator {
  final String quickDir;
  final String customDir;
  final List<_ValidationResult> results = [];

  ConfigValidator({required this.quickDir, required this.customDir});

  factory ConfigValidator.forSystem() {
    final sys = File('tool/config/system.json');
    final data = sys.existsSync() ? jsonDecode(sys.readAsStringSync()) : {};
    final quick = data['paths']?['quick_config'] ?? 'config/quick_config';
    final custom = data['paths']?['custom_config'] ?? 'config/custom_config';
    return ConfigValidator(
      quickDir: quick.replaceAll('/', '\\'), 
      customDir: custom.replaceAll('/', '\\')
    );
  }

  /// Run all schema + dependency checks. Returns true if all pass.
  bool validate() {
    _checkSchema();
    _checkDependencies();
    return results.every((r) => r.passed);
  }

  void printReport() {
    print("\n⚡ Config Validator Report:");
    print("   Source (Quick):  $quickDir");
    print("   Source (Custom): $customDir\n");

    final grouped = <String, List<_ValidationResult>>{};
    for (var r in results) {
      grouped.putIfAbsent(r.file, () => []).add(r);
    }

    for (var file in grouped.keys) {
      final fileResults = grouped[file]!;
      final allPassed = fileResults.every((r) => r.passed);
      print("  ${allPassed ? '✅' : '❌'} ${file}");
      for (var r in fileResults) {
        if (!r.passed) print("      └─ ⚠️  ${r.message}");
      }
    }

    final passed = results.where((r) => r.passed).length;
    final failed = results.where((r) => !r.passed).length;
    print("\n  Summary: $passed passed, $failed failed.");
  }

  // ─────────────────────────────── Schema Checks ──────────────────────────────
  void _checkSchema() {
    _checkFileAsMap(quickDir, 'screens.json', requiredKey: 'screens');
    _checkFileAsMap(quickDir, 'widgets.json', requiredKey: 'widgets');
    _checkFileAsMap(quickDir, 'nav.json', requiredKey: 'routes');
    _checkFileAsMap(quickDir, 'model.json', requiredKey: 'models');
    _checkTheme();

    // Global Files verification
    _checkFileAsMap(customDir, 'api.json', requiredKey: 'api');
    _checkFileAsMap(customDir, 'repository.json', requiredKey: null); 
    _checkFileAsMap(customDir, 'datasource.json', requiredKey: 'datasource');
    _checkFileAsMap(customDir, 'bloc.json', requiredKey: 'bloc');
    _checkFileAsMap(customDir, 'layout.json', requiredKey: 'layouts');
  }

  void _checkFileAsMap(String dir, String fileName, {String? requiredKey}) {
    final file = File('$dir\\$fileName');
    if (!file.existsSync()) {
      results.add(_ValidationResult(fileName, false, 'File missing in $dir'));
      return;
    }
    try {
      final data = jsonDecode(file.readAsStringSync());
      if (data is! Map) {
        results.add(_ValidationResult(fileName, false, 'Expected a JSON Object'));
      } else if (requiredKey != null && !data.containsKey(requiredKey)) {
        results.add(_ValidationResult(fileName, false, "Missing required key: '$requiredKey'"));
      } else {
        results.add(_ValidationResult(fileName, true, "Valid structure"));
      }
    } catch (e) {
      results.add(_ValidationResult(fileName, false, 'Invalid JSON: $e'));
    }
  }

  void _checkTheme() {
    const fileName = 'theme.json';
    final file = File('$customDir\\$fileName');
    if (!file.existsSync()) {
      results.add(_ValidationResult(fileName, false, 'File missing in $customDir'));
      return;
    }
    try {
      final data = jsonDecode(file.readAsStringSync()) as Map;
      final hasLight = data.containsKey('light');
      if (!hasLight) {
        results.add(_ValidationResult(fileName, false, "Missing 'light' theme definition"));
      } else {
        results.add(_ValidationResult(fileName, true, "Theme found"));
      }
    } catch (e) {
      results.add(_ValidationResult(fileName, false, 'Invalid JSON: $e'));
    }
  }

  // ─────────────────────────────── Dependency Checks ──────────────────────────
  void _checkDependencies() {
    final screensFile = File('$quickDir\\screens.json');
    final widgetsFile = File('$quickDir\\widgets.json');
    if (!screensFile.existsSync() || !widgetsFile.existsSync()) return;

    try {
      final screens = List<Map>.from(jsonDecode(screensFile.readAsStringSync())['screens']);
      final widgets = List<Map>.from(jsonDecode(widgetsFile.readAsStringSync())['widgets']);
      final widgetIds = widgets.map((w) => w['id']).toSet();

      final screenIds = screens.map((s) => s['id']).toSet();
      final missing = <String>[];
      for (var screen in screens) {
        final slots = screen['layout']?['slots'] as List?;
        final isTabs = screen['layout']?['type'] == 'tabs';
        
        if (slots != null) {
          for (var slot in slots) {
            final id = slot['id'];
            if (isTabs) {
               // In tabs, it can be a widget OR a screen
               if (!widgetIds.contains(id) && !screenIds.contains(id)) {
                 missing.add("Screen '${screen['id']}' (tabs) refers to unknown ID '${id}' (must be widget or screen)");
               }
            } else if (!widgetIds.contains(id)) {
               missing.add("Screen '${screen['id']}' refers to missing widget '${id}'");
            }
          }
        }
      }

      if (missing.isEmpty) {
        results.add(_ValidationResult('Cross: screens → widgets', true, 'All slots reference valid widgets'));
      } else {
        for (var m in missing) {
          results.add(_ValidationResult('Cross: screens → widgets', false, m));
        }
      }
    } catch (e) {
      results.add(_ValidationResult('Dependency Check', false, 'Failed: $e'));
    }
  }
}

class _ValidationResult {
  final String file;
  final bool passed;
  final String message;
  _ValidationResult(this.file, this.passed, this.message);
}

