import 'package:analyzer/dart/ast/ast.dart';
import '../reverse_config.dart';
import '../parser/dart_source_parser.dart';
import '../parser/ast_helpers.dart';
import '../parser/node_lifter.dart';
import 'pass.dart';

/// Lifts each widget class's `build()` into a node tree → `widgets.json`, and
/// derives a geometry summary → `layout.json` (extra output). Reuses the
/// Thunder WidgetNode vocabulary via NodeLifter.
class WidgetsPass implements ReversePass {
  @override
  String get id => 'widgets';
  @override
  String get outputFile => 'widgets.json';

  static const _widgetBases = {'StatelessWidget', 'StatefulWidget', 'State'};
  static const _geometryTypes = {
    'Padding', 'SizedBox', 'Container', 'Expanded', 'Center', 'Align',
    'Column', 'Row', 'Flexible', 'AspectRatio', 'FractionallySizedBox',
  };

  @override
  PassResult run(ScanContext ctx) {
    final units = (ctx.shared['units'] as List).cast<ParsedDart>();
    final lifter = NodeLifter();
    final trees = <Map<String, dynamic>>[];
    final layout = <Map<String, dynamic>>[];

    for (final pd in units) {
      for (final c in pd.unit.declarations.whereType<ClassDeclaration>()) {
        final base = superclassName(c);
        if (base == null || !_widgetBases.contains(base)) continue;

        final build = c.members.whereType<MethodDeclaration>().where((m) => m.name.lexeme == 'build').firstOrNull;
        if (build == null) continue;

        final tree = lifter.lift(NodeLifter.buildReturnOf(build));
        if (tree == null) continue;

        // A State<Foo> build belongs to widget Foo; otherwise the class itself.
        final owner = stateTargetName(c) ?? c.name.lexeme;
        trees.add({
          'owner': owner,
          'tree': tree,
          'provenance': pd.provenanceAt(c.offset).toJson(),
        });
        _collectGeometry(owner, tree, layout);
      }
    }

    trees.sort((a, b) => (a['owner'] as String).compareTo(b['owner'] as String));
    return PassResult(
      'widgets',
      outputFile,
      {'widgets': trees},
      itemCount: trees.length,
      extraOutputs: {
        'layout.json': {'nodes': layout},
      },
      notes: ['${layout.length} geometry node(s)'],
    );
  }

  void _collectGeometry(String owner, Map<String, dynamic> node, List<Map<String, dynamic>> out) {
    final type = node['type'] as String?;
    final props = node['props'] as Map<String, dynamic>?;
    if (type != null && _geometryTypes.contains(type) && props != null && props.isNotEmpty) {
      out.add({'owner': owner, 'type': type, 'props': props});
    }
    final children = node['children'] as List?;
    if (children != null) {
      for (final c in children) {
        if (c is Map<String, dynamic>) _collectGeometry(owner, c, out);
      }
    }
  }
}
