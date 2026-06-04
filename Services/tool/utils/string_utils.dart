class StringUtils {
  /// Capitalize first character only: 'hello' → 'Hello'
  static String capitalize(String s) =>
      s.isEmpty ? s : s[0].toUpperCase() + s.substring(1);

  /// Convert snake_case to PascalCase: 'map_view' → 'MapView', 'trip_detail' → 'TripDetail'
  static String toPascalCase(String s) =>
      s.split('_').map((e) => e.isEmpty ? e : e[0].toUpperCase() + e.substring(1)).join();

  /// Convert to camelCase (same as toPascalCase for now, used by layout/widget builders)
  static String toCamelCase(String s) => toPascalCase(s);

  /// Convert to snake_case: 'MapView' → 'map_view'
  static String toSnakeCase(String s) {
    return s
        .replaceAllMapped(RegExp(r'([A-Z])'), (match) => '_${match.group(0)!.toLowerCase()}')
        .replaceFirst('_', '');
  }

  /// Convert to a safe filename: lowercase, underscores
  static String toFileName(String s) => s.toLowerCase().replaceAll(' ', '_');

  /// Escape a string for use inside Dart single-quoted strings.
  /// Escapes $ (prevents interpolation) and ' (prevents string termination).
  static String escapeDartString(String s) =>
      s.replaceAll(r'\', r'\\').replaceAll(r'$', r'\$').replaceAll("'", "\\'");
}
