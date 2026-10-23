import 'package:analyzer/dart/ast/ast.dart';
import '../reverse_config.dart';
import '../parser/dart_source_parser.dart';
import '../parser/ast_helpers.dart';
import 'pass.dart';

/// Extracts screen/page objects → `screens.json`.
///
/// Heuristic (syntactic, R1): a class is a screen when it (or its paired
/// `State<T>`) builds a `Scaffold` and is NOT the app root (does not build
/// `MaterialApp`). Route + type are inferred from the class name.
class ScreensPass implements ReversePass {
  @override
  String get id => 'screens';
  @override
  String get outputFile => 'screens.json';

  static const _widgetBases = {'StatelessWidget', 'StatefulWidget'};
  static const _suffixes = ['Screen', 'Page', 'View'];

  @override
  PassResult run(ScanContext ctx) {
    final units = (ctx.shared['units'] as List).cast<ParsedDart>();
    final screens = <Map<String, dynamic>>[];

    for (final pd in units) {
      final classes = pd.unit.declarations.whereType<ClassDeclaration>().toList();

      // Map widget name → does its State build a Scaffold? (stateful screens)
      final stateBuildsScaffold = <String, bool>{};
      for (final c in classes) {
        final target = stateTargetName(c);
        if (target != null && subtreeBuilds(c, 'Scaffold')) {
          stateBuildsScaffold[target] = true;
        }
      }

      for (final c in classes) {
        final base = superclassName(c);
        if (!_widgetBases.contains(base)) continue;

        final name = c.name.lexeme;
        final buildsScaffold = subtreeBuilds(c, 'Scaffold') || (stateBuildsScaffold[name] ?? false);
        final isAppRoot = subtreeBuilds(c, 'MaterialApp');
        if (!buildsScaffold || isAppRoot) continue;

        screens.add({
          'name': name,
          'route': _routeFor(name),
          'type': _typeFor(name),
          'stateful': base == 'StatefulWidget',
          'provenance': pd.provenanceAt(c.offset).toJson(),
        });
      }
    }

    screens.sort((a, b) => (a['name'] as String).compareTo(b['name'] as String));
    return PassResult('screens', outputFile, {'screens': screens}, itemCount: screens.length);
  }

  String _routeFor(String name) {
    var n = name;
    for (final s in _suffixes) {
      if (n.endsWith(s)) {
        n = n.substring(0, n.length - s.length);
        break;
      }
    }
    return '/${_snake(n)}';
  }

  String _typeFor(String name) {
    final lower = name.toLowerCase();
    if (lower.contains('home') || lower.contains('main') || lower.contains('dashboard')) return 'main';
    return 'sub';
  }

  String _snake(String s) => s
      .replaceAllMapped(RegExp(r'(?<=[a-z0-9])[A-Z]'), (m) => '_${m.group(0)}')
      .toLowerCase();
}
