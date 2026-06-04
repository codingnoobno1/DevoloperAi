import 'dart:convert';
import 'dart:io';

/// Theme Engine: Handles theme template merging and placeholder resolution.
class ThemeEngine {
  final Map<String, dynamic> template;

  ThemeEngine(this.template);

  factory ThemeEngine.load(String path) {
    final file = File(path);
    if (!file.existsSync()) return ThemeEngine({});
    return ThemeEngine(Map<String, dynamic>.from(jsonDecode(file.readAsStringSync())));
  }

  /// Resolve a theme by merging the template with user overrides
  /// and resolving placeholders like {primary}.
  Map<String, dynamic> resolve(Map<String, dynamic> userTheme, {required String primaryColor}) {
    final merged = _deepMerge(template, userTheme);
    return _resolvePlaceholders(merged, {"primary": primaryColor});
  }

  Map<String, dynamic> _deepMerge(Map<String, dynamic> base, Map<String, dynamic> overrides) {
    final result = Map<String, dynamic>.from(base);
    overrides.forEach((k, v) {
      if (v is Map && result[k] is Map) {
        result[k] = _deepMerge(Map<String, dynamic>.from(result[k]), Map<String, dynamic>.from(v));
      } else {
        result[k] = v;
      }
    });
    return result;
  }

  Map<String, dynamic> _resolvePlaceholders(Map<String, dynamic> data, Map<String, String> values) {
    final result = <String, dynamic>{};
    data.forEach((k, v) {
      if (v is String) {
        String val = v;
        values.forEach((pk, pv) {
          val = val.replaceAll("{$pk}", pv);
        });
        result[k] = val;
      } else if (v is Map) {
        result[k] = _resolvePlaceholders(Map<String, dynamic>.from(v), values);
      } else {
        result[k] = v;
      }
    });
    return result;
  }
}
