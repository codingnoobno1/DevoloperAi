/// Centralized import builder for generated Dart files.
/// Guarantees no backtick/nimport corruption, deduplication, and sorted output.
class ImportManager {
  final Set<String> _imports = {};

  /// Add a relative import path (e.g., '../bloc/home_cubit.dart')
  void add(String path) {
    _imports.add(path);
  }

  /// Add a package import (e.g., 'flutter/material.dart')
  void addPackage(String pkg) {
    _imports.add('package:$pkg');
  }

  /// Build the final import block as a clean, sorted, newline-separated string.
  String build() {
    if (_imports.isEmpty) return '';
    final sorted = _imports.toList()..sort((a, b) {
      // Package imports first, then relative
      final aIsPackage = a.startsWith('package:');
      final bIsPackage = b.startsWith('package:');
      if (aIsPackage && !bIsPackage) return -1;
      if (!aIsPackage && bIsPackage) return 1;
      return a.compareTo(b);
    });
    return sorted.map((i) => "import '$i';").join('\n');
  }

  /// Whether any imports have been added.
  bool get isEmpty => _imports.isEmpty;

  /// Number of imports.
  int get length => _imports.length;
}
