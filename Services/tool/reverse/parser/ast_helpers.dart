import 'package:analyzer/dart/ast/ast.dart';
import 'package:analyzer/dart/ast/visitor.dart';

/// Version-tolerant helpers over the analyzer AST. Kept in one place so the
/// (occasionally churny) analyzer API surface is touched in exactly one file.

/// Simple type name of a NamedType (e.g. `Scaffold`, `StatelessWidget`).
String namedTypeName(NamedType t) => t.name2.lexeme;

/// Constructor/type name of an instance creation (`Scaffold(...)` → 'Scaffold').
String constructorName(InstanceCreationExpression n) => namedTypeName(n.constructorName.type);

/// Superclass simple name of a class, or null if none.
String? superclassName(ClassDeclaration c) {
  final s = c.extendsClause?.superclass;
  return s == null ? null : namedTypeName(s);
}

/// If [c] is a `State<Foo>` class, returns `Foo`; else null.
String? stateTargetName(ClassDeclaration c) {
  final s = c.extendsClause?.superclass;
  if (s == null || namedTypeName(s) != 'State') return null;
  final args = s.typeArguments?.arguments;
  if (args == null || args.isEmpty) return null;
  final first = args.first;
  return first is NamedType ? namedTypeName(first) : null;
}

/// True if [name] looks like a widget/constructor (Uppercase initial).
bool looksLikeType(String name) => name.isNotEmpty && name[0] == name[0].toUpperCase() && name[0] != '_';

/// Collects every constructor/type name built within a subtree — used to answer
/// "does this class build a Scaffold?" without full resolution.
///
/// IMPORTANT: in a *syntactic* parse, non-const constructor calls like
/// `Scaffold(...)` are parsed as [MethodInvocation] (the parser can't know
/// Scaffold is a class), while `const Text(...)` becomes an
/// [InstanceCreationExpression]. We therefore collect both, treating a
/// capitalized MethodInvocation as a probable constructor.
class ConstructorNameCollector extends RecursiveAstVisitor<void> {
  final Set<String> names = {};

  @override
  void visitInstanceCreationExpression(InstanceCreationExpression node) {
    names.add(constructorName(node));
    super.visitInstanceCreationExpression(node);
  }

  @override
  void visitMethodInvocation(MethodInvocation node) {
    // Unprefixed, capitalized call with no target → likely a constructor.
    if (node.target == null && looksLikeType(node.methodName.name)) {
      names.add(node.methodName.name);
    }
    super.visitMethodInvocation(node);
  }
}

/// True if [node]'s subtree constructs a widget named [widget].
bool subtreeBuilds(AstNode node, String widget) {
  final c = ConstructorNameCollector();
  node.accept(c);
  return c.names.contains(widget);
}
