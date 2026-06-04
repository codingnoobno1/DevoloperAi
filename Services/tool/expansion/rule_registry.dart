import 'dart:convert';
import 'dart:io';

/// Loads, validates, and provides access to expansion rules.
/// Acts as the registry for all AST-based expansion definitions.
class RuleRegistry {
  final Map<String, dynamic> _widgetRules;
  final Map<String, dynamic> _layoutRules;
  final Map<String, dynamic> _navigationRules;
  final Map<String, dynamic> _globalWidgets;

  RuleRegistry._({
    required Map<String, dynamic> widgetRules,
    required Map<String, dynamic> layoutRules,
    required Map<String, dynamic> navigationRules,
    required Map<String, dynamic> globalWidgets,
  })  : _widgetRules = widgetRules,
        _layoutRules = layoutRules,
        _navigationRules = navigationRules,
        _globalWidgets = globalWidgets;

  /// Load rules from a JSON file.
  factory RuleRegistry.load(String path) {
    final file = File(path);
    if (!file.existsSync()) {
      print("⚠️ No expansion rules found at $path. Using defaults.");
      return RuleRegistry._(
        widgetRules: {},
        layoutRules: {},
        navigationRules: {},
        globalWidgets: {},
      );
    }
    final json = Map<String, dynamic>.from(jsonDecode(file.readAsStringSync()));
    return RuleRegistry._(
      widgetRules: Map<String, dynamic>.from(json['widgets'] ?? {}),
      layoutRules: Map<String, dynamic>.from(json['layouts'] ?? {}),
      navigationRules: Map<String, dynamic>.from(json['navigation'] ?? {}),
      globalWidgets: Map<String, dynamic>.from(json['global_widgets'] ?? {}),
    );
  }

  // ─── Widget rules ────────────────────────────────────────

  /// Get the full rule for a widget type (e.g. "map", "card").
  Map<String, dynamic> widgetRule(String type) {
    if (_widgetRules.containsKey(type)) {
      return Map<String, dynamic>.from(_widgetRules[type]);
    }
    // Fallback: a bare container
    return {"ast": {"type": "ContainerNode"}};
  }

  /// Get just the AST definition for a widget type.
  Map<String, dynamic>? widgetAST(String type) {
    final rule = widgetRule(type);
    if (rule['ast'] != null) {
      return Map<String, dynamic>.from(rule['ast']);
    }
    return null;
  }

  /// Check if a widget type should auto-create a sub-screen.
  bool shouldCreateSubScreen(String type) {
    final rule = widgetRule(type);
    return rule['subScreen'] == true;
  }

  /// Check if a widget type should use dynamic routing.
  bool isDynamic(String type) {
    final rule = widgetRule(type);
    return rule['dynamic'] == true;
  }

  // ─── Layout rules ───────────────────────────────────────

  /// Get layout rule for a given screen ID or type.
  /// Returns either a string (legacy builder name) or a Map with AST.
  dynamic layoutRule(String key) {
    return _layoutRules[key];
  }

  /// Get AST for a layout, if it exists.
  Map<String, dynamic>? layoutAST(String key) {
    final rule = _layoutRules[key];
    if (rule is Map) {
      final ruleMap = Map<String, dynamic>.from(rule);
      if (ruleMap['ast'] != null) {
        return Map<String, dynamic>.from(ruleMap['ast']);
      }
    }
    return null;
  }

  /// Get the layout builder type string (legacy support).
  String layoutType(String key) {
    final rule = _layoutRules[key];
    if (rule is String) return rule;
    if (rule is Map && rule['type'] != null) return rule['type'];
    return 'Column';
  }

  // ─── Navigation rules ───────────────────────────────────

  String get subRoutePattern =>
      _navigationRules['subRoutePattern'] ?? '/:parent/:child';

  String get dynamicRoutePattern =>
      _navigationRules['dynamicPattern'] ?? '/:parent/:child/:id';

  String resolveRoute(String parent, String child, {bool dynamic = false}) {
    final pattern = dynamic ? dynamicRoutePattern : subRoutePattern;
    return pattern
        .replaceAll(':parent', parent)
        .replaceAll(':child', child);
  }

  // ─── Global widgets ─────────────────────────────────────

  Map<String, dynamic> get globalWidgets => _globalWidgets;

  /// Get AST for a global widget.
  Map<String, dynamic>? globalWidgetAST(String key) {
    final w = _globalWidgets[key];
    if (w is Map && w['ast'] != null) {
      return Map<String, dynamic>.from(w['ast']);
    }
    return null;
  }

  // ─── Debugging ──────────────────────────────────────────

  void printStats() {
    print("📏 Rule Registry:");
    print("   Widget rules:  ${_widgetRules.length}");
    print("   Layout rules:  ${_layoutRules.length}");
    print("   Nav rules:     ${_navigationRules.length}");
    print("   Global widgets: ${_globalWidgets.length}");
    
    int astCount = 0;
    _widgetRules.forEach((k, v) {
      if (v is Map && v['ast'] != null) astCount++;
    });
    print("   AST-defined widgets: $astCount / ${_widgetRules.length}");
  }
}
