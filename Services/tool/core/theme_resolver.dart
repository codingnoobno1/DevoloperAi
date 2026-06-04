import 'models.dart';

class ThemeResolver {
  static String resolveColor(String? colorHex, AppModel model) {
    if (colorHex == null) return "Colors.blue";
    if (colorHex.startsWith('#')) {
      return "Color(0xFF\${colorHex.substring(1)})";
    }
    
    // Check if it's a theme key
    final themeColors = model.theme['colors'] as Map?;
    if (themeColors != null && themeColors.containsKey(colorHex)) {
      return "Theme.of(context).colorScheme.\$colorHex";
    }

    return "Colors.grey";
  }

  static String resolveTextStyle(String? variant, AppModel model) {
    switch (variant) {
      case 'heading': return "Theme.of(context).textTheme.headlineMedium";
      case 'title': return "Theme.of(context).textTheme.titleLarge";
      case 'body': return "Theme.of(context).textTheme.bodyMedium";
      case 'caption': return "Theme.of(context).textTheme.bodySmall";
      default: return "Theme.of(context).textTheme.bodyMedium";
    }
  }
}
