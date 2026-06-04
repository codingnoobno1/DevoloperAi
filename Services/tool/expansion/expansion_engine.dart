import 'dart:convert';
import 'dart:io';

import '../utils/logger.dart';
import '../core/mode_manager.dart';
import 'rule_registry.dart';
import 'template_resolver.dart';
import 'naming_engine.dart';
import 'router_engine.dart';
import 'theme_engine.dart';

/// The Expansion Engine v5.0: Structured Noob Architecture.
/// Transforms minimalist mainapp.json into 4 typed IR files:
/// 1. screens.json (Layout + Slots)
/// 2. widgets.json (Typed UI + Data Binding)
/// 3. nav.json (Strict ID Routes)
/// 4. model.json (Data Schema)
class ExpansionEngine {
  static const String systemPath = 'tool/config/system.json';
  static const String rulesPath = 'tool/config/expansion_rules.json';
  static const String templatesPath = 'tool/config/templates.json';
  static const String themeTemplatePath = 'tool/config/themes/default_theme.json';

  /// Expands the minimalist mainapp.json into a structured IR map.
  static Map<String, dynamic> expandToMemory(String inputPath, {bool persist = false, bool force = false}) {
    if (ModeManager.isPro() && !force) {
      Logger.warn("⚠️  Pro Mode active. Expansion skipped to protect custom_config/.");
      return {};
    }

    final file = File(inputPath);
    if (!file.existsSync()) return {};

    Map<String, dynamic> mainApp = jsonDecode(file.readAsStringSync());
    Logger.info("⚡ Structured Expansion Started (Noob v5)...");

    final system = _loadJson(systemPath);
    final expandedDir = system['paths']?['custom_config'] ?? system['paths']?['expanded'] ?? 'config/custom_config/';
    
    final themeEngine = ThemeEngine.load(themeTemplatePath);

    // 1. Core Expansion Logic
    final result = _expandStructuredArchitecture(mainApp);

    // 2. Theme Resolution
    final primary = mainApp['theme']?['primary'] ?? mainApp['app']?['primary'] ?? '#009688';
    final theme = themeEngine.resolve(
      Map<String, dynamic>.from(mainApp['theme'] ?? {}),
      primaryColor: primary,
    );

    final expandedConfig = {
      "screens": result.screens,
      "widgets": result.widgets,
      "nav": result.nav,
      "models": result.models,
      "layouts": result.layouts,
      "bloc": result.bloc,
      "datasource": result.datasource,
      "theme": theme,
      "meta": {
        "mode": "noob",
        "engine": "structured_expansion_v5",
        "version": "5.0",
      }
    };

    if (persist) {
      _persistToDisk(expandedDir, expandedConfig);
    }

    return expandedConfig;
  }

  static void _persistToDisk(String dir, Map<String, dynamic> config) {
    // 1. Load system paths
    final system = _loadJson(systemPath);
    final quickDir = system['paths']?['quick_config'] ?? 'config/quick_config/';
    final depthDir = system['paths']?['custom_config'] ?? 'config/custom_config/';

    Directory(quickDir).createSync(recursive: true);
    Directory(depthDir).createSync(recursive: true);
    
    // 1. Top Layer (Noob IR) -> quick_config/
    File('$quickDir/screens.json').writeAsStringSync(jsonEncode({"screens": config['screens']}));
    File('$quickDir/widgets.json').writeAsStringSync(jsonEncode({"widgets": config['widgets']}));
    File('$quickDir/nav.json').writeAsStringSync(jsonEncode(config['nav']));
    File('$quickDir/model.json').writeAsStringSync(jsonEncode({"models": config['models']}));
    
    // 2. Depth Layer (Expanded IR) -> custom_config/
    File('$depthDir/screentype.json').writeAsStringSync(jsonEncode({"screenTypes": config['screens']}));
    File('$depthDir/layout.json').writeAsStringSync(jsonEncode({"layouts": config['layouts'] ?? {}}));
    File('$depthDir/dynamicwidget.json').writeAsStringSync(jsonEncode({"dynamicWidgets": config['widgets']}));
    File('$depthDir/navigation.json').writeAsStringSync(jsonEncode({"navigation": config['nav']}));
    File('$depthDir/bloc.json').writeAsStringSync(jsonEncode({"bloc": config['bloc'] ?? {}}));
    File('$depthDir/condition.json').writeAsStringSync(jsonEncode({"condition": config['condition'] ?? {}}));
    File('$depthDir/datasource.json').writeAsStringSync(jsonEncode({"datasource": config['datasource'] ?? {}}));
    
    File('$depthDir/theme.json').writeAsStringSync(jsonEncode(config['theme']));
    File('$depthDir/meta.json').writeAsStringSync(jsonEncode({
      ...config['meta'] as Map,
      "generatedAt": DateTime.now().toIso8601String(),
      "locked": false,
    }));

    // Legacy sync for global files
    _syncGlobalFile('api.json', dir);
    _syncGlobalFile('repository.json', dir);
    _syncGlobalFile('datasource.json', dir);

    Logger.success("✅ Expansion complete: $dir [4 typed files generated]");
  }

  static void _syncGlobalFile(String fileName, String targetDir) {
    final source = File('config/$fileName');
    final target = File('$targetDir/$fileName');
    if (source.existsSync() && !target.existsSync()) {
      source.copySync(target.path);
    }
  }

  /// Implements the "Structured but Simple" expansion logic.
  static _ExpandedResult _expandStructuredArchitecture(Map<String, dynamic> mainApp) {
    final List<Map<String, dynamic>> screens = [];
    final List<Map<String, dynamic>> widgets = [];
    final List<Map<String, dynamic>> routes = [];
    final List<Map<String, dynamic>> models = [];

    final appScreens = mainApp['screens'] as Map<String, dynamic>? ?? {};
    final entryScreen = mainApp['entry'] ?? appScreens.keys.firstOrNull ?? 'home';

    // 1. Process Screens & Widgets
    appScreens.forEach((screenId, widgetIds) {
      final List<Map<String, dynamic>> slots = [];
      if (widgetIds is List) {
        for (int i = 0; i < widgetIds.length; i++) {
          final wId = widgetIds[i].toString();
          slots.add({"id": wId, "position": i + 1});

          // Build Structured Widget
          widgets.add({
            "id": wId,
            "type": _inferWidgetType(wId),
            "template": _inferTemplate(wId),
            "data": _inferDataBinding(wId, mainApp)
          });
        }
      }

      screens.add({
        "id": screenId,
        "layout": {
          "type": _inferLayoutType(screenId),
          "template": _inferLayoutTemplate(screenId),
          "slots": slots
        },
        "data": { "type": "static" }
      });
    });

    // 2. Build Nav Routes
    appScreens.forEach((fromId, _) {
       routes.add({
         "id": "${fromId}_nav",
         "from": fromId,
         "to": entryScreen == fromId ? null : entryScreen
       });
    });

    // 3. Auto-populate models if they exist in mainApp
    if (mainApp.containsKey('models')) {
      models.addAll(List<Map<String, dynamic>>.from(mainApp['models']));
    }

    return _ExpandedResult(
      screens: screens,
      widgets: widgets,
      nav: {
        "entry": entryScreen,
        "routes": routes
      },
      models: models,
      layouts: {
        "header_body": {
          "type": "vertical",
          "properties": { 
            "spacing": 16.0,
            "padding": [16, 16, 16, 16]
          }
        },
        "default_stack": {
          "type": "stack"
        }
      },
      bloc: {
        "mappings": {
          for (var s in screens) s['id']: "${s['id']}Cubit"
        }
      },
      datasource: {
        "type": "api",
        "api": {
          "baseUrl": "https://api.rideapp.com"
        }
      }
    );
  }

  static Map<String, dynamic> _inferDataBinding(String id, Map<String, dynamic> mainApp) {
    if (id.contains('offers') || id.contains('trips')) {
      return {
        "type": "api",
        "endpoint": "/$id",
        "model": "${id.replaceAll(RegExp(r's$'), '')}Model"
      };
    }
    return {
      "type": "static",
      "value": {}
    };
  }

  static String _inferLayoutType(String screenId) {
    if (screenId.contains('tabs')) return 'tabs';
    return 'vertical';
  }

  static String _inferLayoutTemplate(String screenId) {
    if (screenId == 'home') return 'header_body';
    return 'default_stack';
  }

  static String _inferWidgetType(String id) {
    if (id.contains('map')) return 'map';
    if (id.contains('list')) return 'list';
    if (id.contains('form')) return 'form';
    if (id.contains('chart')) return 'chart';
    if (id.contains('card') || id.contains('offer')) return 'card';
    return 'container';
  }

  static String _inferTemplate(String id) {
    if (id.contains('map')) return 'full_map';
    if (id.contains('card')) return 'promo_card';
    if (id.contains('history')) return 'history_list';
    return 'default';
  }

  static Map<String, dynamic> _loadJson(String path) {
    final file = File(path);
    return file.existsSync() ? Map<String, dynamic>.from(jsonDecode(file.readAsStringSync())) : {};
  }
}

class _ExpandedResult {
  final List<Map<String, dynamic>> screens;
  final List<Map<String, dynamic>> widgets;
  final Map<String, dynamic> nav;
  final List<Map<String, dynamic>> models;
  final Map<String, dynamic> layouts;
  final Map<String, dynamic> bloc;
  final Map<String, dynamic> datasource;

  _ExpandedResult({
    required this.screens,
    required this.widgets,
    required this.nav,
    required this.models,
    required this.layouts,
    required this.bloc,
    required this.datasource,
  });
}
