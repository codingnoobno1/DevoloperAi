import 'package:analyzer/dart/ast/ast.dart';
import 'ast_helpers.dart';

/// Lifts a Flutter `build()` widget expression into a JSON tree whose `type`
/// names mirror Thunder's `core/ast.dart` WidgetNode vocabulary
/// (Scaffold, Column, Row, Container, Text, ...). This is the reverse of the
/// forward renderer: AST → node tree.
///
/// Syntactic (R2): non-const widgets are MethodInvocations, const ones are
/// InstanceCreationExpressions — both are handled.
class NodeLifter {
  final int maxDepth;
  NodeLifter({this.maxDepth = 40});

  /// Named args whose value is itself a child widget.
  static const _childSlots = {
    'child', 'body', 'appBar', 'title', 'leading', 'trailing', 'home',
    'bottomNavigationBar', 'floatingActionButton', 'flexibleSpace', 'drawer',
  };

  Map<String, dynamic>? lift(Expression? e, {int depth = 0}) {
    if (e == null || depth > maxDepth) return null;

    String? name;
    ArgumentList? args;
    if (e is InstanceCreationExpression) {
      name = constructorName(e);
      args = e.argumentList;
    } else if (e is MethodInvocation && e.target == null && looksLikeType(e.methodName.name)) {
      name = e.methodName.name;
      args = e.argumentList;
    } else {
      return null; // not a widget construction
    }

    final props = <String, dynamic>{};
    final children = <Map<String, dynamic>>[];

    for (final a in args.arguments) {
      if (a is NamedExpression) {
        final label = a.name.label.name;
        final val = a.expression;
        if (_childSlots.contains(label)) {
          final c = lift(val, depth: depth + 1);
          if (c != null) children.add({'slot': label, ...c});
        } else if (label == 'children' && val is ListLiteral) {
          for (final el in val.elements) {
            if (el is Expression) {
              final c = lift(el, depth: depth + 1);
              if (c != null) children.add(c);
            }
          }
        } else {
          final s = _scalar(val);
          if (s != null) props[label] = s;
        }
      } else {
        // Positional: a string (e.g. Text('x')) or a nested widget.
        final s = _scalar(a);
        if (s != null) {
          props.putIfAbsent('text', () => s);
        } else {
          final c = lift(a, depth: depth + 1);
          if (c != null) children.add(c);
        }
      }
    }

    return {
      'type': name,
      if (props.isNotEmpty) 'props': props,
      if (children.isNotEmpty) 'children': children,
    };
  }

  dynamic _scalar(Expression e) {
    if (e is SimpleStringLiteral) return e.value;
    if (e is IntegerLiteral) return e.value;
    if (e is DoubleLiteral) return e.value;
    if (e is BooleanLiteral) return e.value;
    if (e is StringInterpolation) return e.toSource();
    return null;
  }

  /// The widget expression returned by a `build` method, or null.
  static Expression? buildReturnOf(MethodDeclaration build) {
    final body = build.body;
    if (body is ExpressionFunctionBody) return body.expression;
    if (body is BlockFunctionBody) {
      for (final st in body.block.statements) {
        if (st is ReturnStatement) return st.expression;
      }
    }
    return null;
  }
}
