import 'dart:convert';
import 'dart:io';

/// Router Engine: Resolves navigation and routing patterns.
class RouterEngine {
  final Map<String, String> patterns;

  RouterEngine(this.patterns);

  factory RouterEngine.load(String path) {
    final file = File(path);
    if (!file.existsSync()) return RouterEngine({});
    final json = Map<String, dynamic>.from(jsonDecode(file.readAsStringSync()));
    final p = Map<String, dynamic>.from(json['patterns'] ?? {});
    return RouterEngine(p.map((k, v) => MapEntry(k, v.toString())));
  }

  /// Resolve a route path based on a pattern type (e.g. 'sub', 'dynamic')
  /// and replacement data.
  String resolve(String type, Map<String, String> data) {
    String? pattern = patterns[type];
    if (pattern == null) return "/${data['child'] ?? data['feature']}";
    
    data.forEach((k, v) {
      pattern = pattern!.replaceAll("{$k}", v);
    });
    return pattern!;
  }
}
