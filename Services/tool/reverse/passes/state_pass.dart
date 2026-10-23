import 'package:analyzer/dart/ast/ast.dart';
import 'package:analyzer/dart/ast/visitor.dart';
import '../reverse_config.dart';
import '../parser/dart_source_parser.dart';
import '../parser/ast_helpers.dart';
import 'pass.dart';

/// Extracts state management → `bloc.json`: Bloc/Cubit classes, their state
/// type, `on<Event>` handlers and `emit(...)` count. ChangeNotifier is captured
/// as `kind: notifier`. Syntactic (R4).
class StatePass implements ReversePass {
  @override
  String get id => 'state';
  @override
  String get outputFile => 'bloc.json';

  @override
  PassResult run(ScanContext ctx) {
    final units = (ctx.shared['units'] as List).cast<ParsedDart>();
    final blocs = <Map<String, dynamic>>[];

    for (final pd in units) {
      for (final c in pd.unit.declarations.whereType<ClassDeclaration>()) {
        final base = superclassName(c);
        String? kind;
        if (base == 'Bloc') kind = 'bloc';
        else if (base == 'Cubit') kind = 'cubit';
        else if (base == 'ChangeNotifier') kind = 'notifier';
        if (kind == null) continue;

        final typeArgs = c.extendsClause?.superclass.typeArguments?.arguments ?? const [];
        final stateType = typeArgs.isNotEmpty ? typeArgs.last.toSource() : null;
        final eventType = (kind == 'bloc' && typeArgs.length >= 2) ? typeArgs.first.toSource() : null;

        final v = _StateVisitor();
        c.accept(v);

        blocs.add({
          'name': c.name.lexeme,
          'kind': kind,
          if (eventType != null) 'eventType': eventType,
          if (stateType != null) 'stateType': stateType,
          'events': v.handledEvents.toList()..sort(),
          'emits': v.emitCount,
          'provenance': pd.provenanceAt(c.offset).toJson(),
        });
      }
    }

    blocs.sort((a, b) => (a['name'] as String).compareTo(b['name'] as String));
    return PassResult('state', outputFile, {'blocs': blocs}, itemCount: blocs.length);
  }
}

class _StateVisitor extends RecursiveAstVisitor<void> {
  final Set<String> handledEvents = {};
  int emitCount = 0;

  @override
  void visitMethodInvocation(MethodInvocation node) {
    final name = node.methodName.name;
    if (name == 'emit') emitCount++;
    if (name == 'on') {
      // on<CounterIncremented>((e, emit) => ...)
      final ta = node.typeArguments?.arguments;
      if (ta != null && ta.isNotEmpty) handledEvents.add(ta.first.toSource());
    }
    super.visitMethodInvocation(node);
  }
}
