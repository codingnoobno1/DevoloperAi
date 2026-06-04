import '../core/dependency_graph.dart';
import '../core/file_writer.dart';
import '../core/import_manager.dart';
import '../core/models.dart';
import '../utils/string_utils.dart';
import 'base_generator.dart';

class RouteGenerator implements BaseGenerator {
  DependencyGraph? graph;

  @override
  Future<void> generate(AppModel model) async {
    final dg = graph ?? DependencyGraph()..resolve(model);
    final imports = ImportManager();
    imports.addPackage('flutter/material.dart');

    final List<String> routeConstants = [];
    final List<String> routeCases = [];

    // Process all screens
    for (final screen in model.screens) {
      final screenName = screen.name.toLowerCase();
      final feature = dg.screenToFeature[screenName] ?? screenName;
      final className = StringUtils.toPascalCase(screenName);
      
      // Check for custom path in navigation.routes
      final path = model.navigation.routes[screenName] ?? '/$screenName';
      
      routeConstants.add("  static const String $screenName = '$path';");
      
      imports.add('../../features/$feature/view/${screenName}_screen.dart');
      routeCases.add("""
      case $screenName:
        return MaterialPageRoute(builder: (_) => const ${className}Screen());""");
    }

    final code = """
${imports.build()}

class AppRoutes {
${routeConstants.join('\n')}

  static Route<dynamic> generateRoute(RouteSettings routeSettings) {
    switch (routeSettings.name) {
      case '/':
${model.navigation.entry != '' ? "        return MaterialPageRoute(builder: (_) => const ${StringUtils.toPascalCase(model.navigation.entry)}Screen());" : "        return MaterialPageRoute(builder: (_) => const ${StringUtils.toPascalCase(model.screens.first.id)}Screen());"}
${routeCases.join('\n')}
      default:
        return MaterialPageRoute(
          builder: (_) => Scaffold(
            body: Center(child: Text('No route defined for \${routeSettings.name}')),
          ),
        );
    }
  }
}
""";
    FileWriter.write('lib_gen/core/routes/app_routes.dart', code);
  }
}
