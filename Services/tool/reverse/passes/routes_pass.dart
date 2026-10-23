import 'package:analyzer/dart/ast/ast.dart';
import 'package:analyzer/dart/ast/visitor.dart';
import '../reverse_config.dart';
import '../parser/dart_source_parser.dart';
import '../parser/ast_helpers.dart';
import 'pass.dart';

/// Extracts the navigation surface → `nav.json`:
/// declared routes (`MaterialApp.routes`, `home`, `initialRoute`) and route
/// usages (`Navigator.pushNamed('/x')`). Syntactic (R1).
class RoutesPass implements ReversePass {
  @override
  String get id => 'routes';
  @override
  String get outputFile => 'nav.json';

  @override
  PassResult run(ScanContext ctx) {
    final units = (ctx.shared['units'] as List).cast<ParsedDart>();
    final routes = <Map<String, dynamic>>[];
    final usages = <Map<String, dynamic>>[];
    String? initial;

    for (final pd in units) {
      final v = _NavVisitor(pd);
      pd.unit.accept(v);
      routes.addAll(v.routes);
      usages.addAll(v.usages);
      initial ??= v.initialRoute;
    }

    routes.sort((a, b) => (a['path'] as String).compareTo(b['path'] as String));
    return PassResult(
      'routes',
      outputFile,
      {'initialRoute': initial, 'routes': routes, 'usages': usages},
      itemCount: routes.length,
      notes: ['${usages.length} navigation usage(s)'],
    );
  }
}

class _NavVisitor extends RecursiveAstVisitor<void> {
  final ParsedDart pd;
  final List<Map<String, dynamic>> routes = [];
  final List<Map<String, dynamic>> usages = [];
  String? initialRoute;

  _NavVisitor(this.pd);

  @override
  void visitInstanceCreationExpression(InstanceCreationExpression node) {
    // `const MaterialApp(...)` / `new MaterialApp(...)`
    if (constructorName(node) == 'MaterialApp') {
      _handleMaterialApp(node.argumentList);
    }
    super.visitInstanceCreationExpression(node);
  }

  @override
  void visitMethodInvocation(MethodInvocation node) {
    // Bare `MaterialApp(...)` parses as a MethodInvocation in a syntactic parse.
    if (node.target == null && node.methodName.name == 'MaterialApp') {
      _handleMaterialApp(node.argumentList);
    }
    if (node.methodName.name == 'pushNamed') {
      final args = node.argumentList.arguments;
      final path = args.length >= 2 ? _stringOf(args[1]) : (args.isNotEmpty ? _stringOf(args.first) : null);
      if (path != null) {
        usages.add({'path': path, 'provenance': pd.provenanceAt(node.offset).toJson()});
      }
    }
    super.visitMethodInvocation(node);
  }

  void _handleMaterialApp(ArgumentList argList) {
    for (final arg in argList.arguments.whereType<NamedExpression>()) {
      final label = arg.name.label.name;
      final expr = arg.expression;
      switch (label) {
        case 'routes':
          if (expr is SetOrMapLiteral) {
            for (final e in expr.elements.whereType<MapLiteralEntry>()) {
              final path = _stringOf(e.key);
              if (path != null) {
                routes.add({
                  'path': path,
                  'target': _widgetIn(e.value),
                  'source': 'MaterialApp.routes',
                  'provenance': pd.provenanceAt(e.offset).toJson(),
                });
              }
            }
          }
        case 'initialRoute':
          initialRoute = _stringOf(expr);
        case 'home':
          routes.add({
            'path': '/',
            'target': _widgetIn(expr),
            'source': 'MaterialApp.home',
            'provenance': pd.provenanceAt(arg.offset).toJson(),
          });
      }
    }
  }

  String? _stringOf(Expression e) => e is SimpleStringLiteral ? e.value : null;

  /// Best-effort target widget name inside a route builder / home expression.
  String? _widgetIn(AstNode node) {
    final c = ConstructorNameCollector();
    node.accept(c);
    for (final n in c.names) {
      if (RegExp(r'(Screen|Page|View)$').hasMatch(n)) return n;
    }
    return c.names.isNotEmpty ? c.names.first : null;
  }
}
