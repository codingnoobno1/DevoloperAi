import '../core/dependency_graph.dart';
import '../core/file_writer.dart';
import '../core/import_manager.dart';
import '../core/models.dart';
import '../utils/string_utils.dart';
import 'base_generator.dart';

class MainGenerator implements BaseGenerator {
  DependencyGraph? graph;

  @override
  Future<void> generate(AppModel model) async {
    final dg = graph ?? DependencyGraph()..resolve(model);
    final imports = ImportManager();
    imports.addPackage('flutter/material.dart');
    imports.addPackage('flutter_bloc/flutter_bloc.dart');
    imports.add('core/theme/app_theme.dart');
    imports.add('core/navigation/navigation_service.dart');
    imports.add('core/services/api_service.dart');
    imports.add('core/services/db_service.dart');
    imports.add('core/routes/app_routes.dart');
    imports.add('core/ui/ui_factory.dart');

    // Add feature imports for all features
    for (final feature in dg.allFeatures) {
      imports.add('features/$feature/bloc/${feature}_cubit.dart');
      imports.add('features/$feature/repository/${feature}_repository.dart');
    }

    // Build repository providers
    final repoProviders = StringBuffer();
    for (final feature in dg.allFeatures) {
      final className = StringUtils.toPascalCase(feature);
      repoProviders.writeln("""
        RepositoryProvider<${className}Repository>(
          create: (context) => ${className}Repository(context.read<ApiService>(), context.read<DbService>()),
        ),""");
    }

    // Build bloc providers
    final blocProviders = StringBuffer();
    for (final feature in dg.allFeatures) {
      final className = StringUtils.toPascalCase(feature);
      blocProviders.writeln("""
        BlocProvider<${className}Cubit>(
          create: (context) => ${className}Cubit(context.read<${className}Repository>()),
        ),""");
    }

    // Determine initial route from condition or first screen
    String initialRouteCode;
    final entry = model.condition['entry'];
    if (entry != null && entry['true'] != null) {
      initialRouteCode = "AppRoutes.${entry['true']}";
    } else {
      final firstScreen = model.screens.isNotEmpty ? model.screens.first.name : 'home';
      initialRouteCode = "AppRoutes.$firstScreen";
    }

    final uiMode = model.theme['style'] ?? 'material';
    final baseUrl = model.datasource['base_url'] ?? 'https://api.thunder.app';

    final code = """
${imports.build()}

void main() {
  WidgetsFlutterBinding.ensureInitialized();
  UIFactory.init('$uiMode');
  runApp(const ThunderApp());
}

class ThunderApp extends StatelessWidget {
  const ThunderApp({super.key});

  @override
  Widget build(BuildContext context) {
    return MultiRepositoryProvider(
      providers: [
        RepositoryProvider(create: (_) => ApiService(baseUrl: '$baseUrl')),
        RepositoryProvider(create: (_) => DbService()),
$repoProviders
      ],
      child: MultiBlocProvider(
        providers: [
$blocProviders
        ],
        child: MaterialApp(
          title: 'Thunder App',
          debugShowCheckedModeBanner: false,
          theme: AppTheme.lightTheme,
          darkTheme: AppTheme.darkTheme,
          navigatorKey: NavigationService.navigatorKey,
          initialRoute: $initialRouteCode,
          onGenerateRoute: AppRoutes.generateRoute,
        ),
      ),
    );
  }
}
""";
    FileWriter.write('lib_gen/main.dart', code);
  }
}
