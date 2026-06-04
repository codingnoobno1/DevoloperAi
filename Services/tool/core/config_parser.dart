import 'dart:convert';
import 'dart:io';
import '../utils/logger.dart';
import 'models.dart';
import 'mode_manager.dart';

/// ConfigParser v2.0: Unified Configuration Loader.
/// Consistently reads from the 'custom_config/' IR layer.
class ConfigParser {
  static const String systemPath = 'tool/config/system.json';

  /// Parses the configuration from the custom_config directory.
  static AppModel parse() {
    final system = _loadSystem();
    final quickDir = system['paths']?['quick_config'] ?? 'config/quick_config/';
    final sourceDir = system['paths']?['custom_config'] ?? 'config/custom_config/';

    Logger.info("⚡ ${ModeManager.currentMode().toUpperCase()} Mode: Sourcing IR from $quickDir & $sourceDir");

    final Map<String, dynamic> combined = {};

    final configs = {
      'screens.json': 'screens',
      'widgets.json': 'widgets',
      'nav.json': 'navigation',
      'navigation.json': 'navigation',
      'model.json': 'models',
      'theme.json': 'theme',
      'api.json': 'api',
      'repository.json': 'repository',
      'datasource.json': 'datasource',
      'db.json': 'db',
      'condition.json': 'condition',
      'screentype.json': 'screentype',
      'layout.json': 'layouts',
      'dynamicwidget.json': 'dynamic_widgets',
      'bloc.json': 'bloc',
    };

    final topLayer = {'screens.json', 'widgets.json', 'nav.json', 'model.json'};
    final mandatory = {
      'screens.json', 
      'widgets.json', 
      'nav.json', 
      'theme.json',
      'bloc.json',
      'layout.json'
    };

    configs.forEach((fileName, key) {
      final isTop = topLayer.contains(fileName);
      final file = File('${isTop ? quickDir : sourceDir}/$fileName');
      
      if (!file.existsSync()) {
        if (mandatory.contains(fileName)) {
          // Check fallback for top layer in sourceDir if not in quickDir (Pro migration support)
          final fallback = File('$sourceDir/$fileName');
          if (fallback.existsSync()) {
             final data = jsonDecode(fallback.readAsStringSync());
             combined[key] = (data is Map && data.length == 1 && data.containsKey(key)) ? data[key] : data;
             return;
          }

          throw Exception("❌ Mandatory config file missing: '$fileName'\n"
                          "   Checked: ${file.path}\n"
                          "   Run 'thunder expand' to restore.");
        } else {
          combined[key] = {}; 
          return;
        }
      }

      final data = jsonDecode(file.readAsStringSync());

      if (data is Map && data.length == 1 && data.containsKey(key)) {
        combined[key] = data[key];
      } else {
        combined[key] = data;
      }
    });

    return AppModel.fromJson(combined);
  }

  static Map<String, dynamic> _loadSystem() {
    final file = File(systemPath);
    if (!file.existsSync()) return {};
    return Map<String, dynamic>.from(jsonDecode(file.readAsStringSync()));
  }
}
