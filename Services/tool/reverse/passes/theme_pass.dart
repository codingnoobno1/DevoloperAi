import 'package:analyzer/dart/ast/ast.dart';
import 'package:analyzer/dart/ast/visitor.dart';
import '../reverse_config.dart';
import '../parser/dart_source_parser.dart';
import 'pass.dart';

/// Extracts theme tokens → `theme.json`: `ThemeData(...)` / `ColorScheme(...)`
/// named args and `ColorScheme.fromSeed(seedColor: ...)`. Values are captured as
/// source expressions (e.g. `Colors.indigo`, `Color(0xFF...)`). Syntactic (R4).
class ThemePass implements ReversePass {
  @override
  String get id => 'theme';
  @override
  String get outputFile => 'theme.json';

  @override
  PassResult run(ScanContext ctx) {
    final units = (ctx.shared['units'] as List).cast<ParsedDart>();
    final themes = <Map<String, dynamic>>[];

    for (final pd in units) {
      final v = _ThemeVisitor(pd);
      pd.unit.accept(v);
      themes.addAll(v.themes);
    }

    return PassResult('theme', outputFile, {'themes': themes}, itemCount: themes.length);
  }
}

class _ThemeVisitor extends RecursiveAstVisitor<void> {
  final ParsedDart pd;
  final List<Map<String, dynamic>> themes = [];
  _ThemeVisitor(this.pd);

  static const _themeCtors = {'ThemeData', 'ColorScheme'};

  @override
  void visitMethodInvocation(MethodInvocation node) {
    final name = node.methodName.name;
    // ThemeData(...) / ColorScheme(...) / ColorScheme.fromSeed(...)
    final isSeed = name == 'fromSeed' && node.target?.toSource() == 'ColorScheme';
    if (_themeCtors.contains(name) || isSeed) {
      _capture(isSeed ? 'ColorScheme.fromSeed' : name, node.argumentList, node.offset);
    }
    super.visitMethodInvocation(node);
  }

  void _capture(String source, ArgumentList args, int offset) {
    final tokens = <String, dynamic>{};
    for (final a in args.arguments.whereType<NamedExpression>()) {
      tokens[a.name.label.name] = a.expression.toSource();
    }
    if (tokens.isNotEmpty) {
      themes.add({'source': source, 'tokens': tokens, 'provenance': pd.provenanceAt(offset).toJson()});
    }
  }
}
