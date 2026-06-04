import 'dart:convert';
import 'dart:io';

/// Naming Engine: Resolves naming patterns for screens, layouts, and widgets.
class NamingEngine {
  final Map<String, String> rules;

  NamingEngine(this.rules);

  factory NamingEngine.load(String path) {
    final file = File(path);
    if (!file.existsSync()) return NamingEngine({});
    final json = Map<String, dynamic>.from(jsonDecode(file.readAsStringSync()));
    return NamingEngine(json.map((k, v) => MapEntry(k, v.toString())));
  }

  /// Build a name based on a pattern type and replacement values.
  /// Patterns like "{feature}_{widget}" will have placeholders replaced.
  String build(String type, Map<String, String> values) {
    String pattern = rules[type] ?? "{feature}";
    values.forEach((k, v) {
      pattern = pattern.replaceAll("{$k}", v);
    });
    return pattern;
  }
}
