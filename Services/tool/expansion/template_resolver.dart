import 'dart:convert';
import 'dart:io';

/// Loads, merges, and resolves template packs.
/// A template is a pre-defined application blueprint that expands into
/// a full dashboard/tab/widget structure.
class TemplateResolver {
  final Map<String, dynamic> _templates;

  TemplateResolver._(this._templates);

  factory TemplateResolver.load(String path) {
    final file = File(path);
    if (!file.existsSync()) return TemplateResolver._({});
    return TemplateResolver._(
      Map<String, dynamic>.from(jsonDecode(file.readAsStringSync())),
    );
  }

  /// Check if a template name is registered.
  bool has(String name) => _templates.containsKey(name);

  /// Get available template names.
  List<String> get names => _templates.keys.toList();

  /// Apply a template: deep-merge it as a base, with user overrides on top.
  /// Returns the merged configuration.
  Map<String, dynamic> apply(String name, Map<String, dynamic> userConfig) {
    if (!_templates.containsKey(name)) {
      print("⚠️ Template '$name' not found. Available: ${names.join(', ')}");
      return userConfig;
    }

    final base = Map<String, dynamic>.from(_templates[name]);
    print("📦 Applying Template: $name");
    return _deepMerge(base, userConfig);
  }

  /// Deep-merge two maps. Values in [overrides] take precedence.
  /// Nested maps are merged recursively. Lists in overrides replace base lists.
  static Map<String, dynamic> _deepMerge(
    Map<String, dynamic> base,
    Map<String, dynamic> overrides,
  ) {
    final result = Map<String, dynamic>.from(base);
    overrides.forEach((k, v) {
      if (k == 'template') return; // Don't copy the template key itself
      if (v is Map && result[k] is Map) {
        result[k] = _deepMerge(
          Map<String, dynamic>.from(result[k]),
          Map<String, dynamic>.from(v),
        );
      } else {
        result[k] = v;
      }
    });
    return result;
  }
}
