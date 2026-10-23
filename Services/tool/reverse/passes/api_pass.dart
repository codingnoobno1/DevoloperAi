import 'package:analyzer/dart/ast/ast.dart';
import 'package:analyzer/dart/ast/visitor.dart';
import '../reverse_config.dart';
import '../parser/dart_source_parser.dart';
import '../connector/api_model.dart';
import 'pass.dart';

/// Extracts HTTP call sites → `connector/api.json`.
///
/// Detects `dio/http/client/_api.get|post|put|patch|delete(path)` and
/// canonicalizes the path template. The path argument may be a string literal,
/// an interpolation (`'/users/$id'` → `/users/{id}`), OR — critically for the
/// common **ApiClient + Repository + ApiConstants** pattern — a symbolic constant
/// like `ApiConstants.services` / `ApiConstants.serviceDetail(id)`. Those are
/// resolved through a project-wide constants table built from every `static const`
/// string and `static String foo(id) => '...'` member (interpolation between
/// constants is resolved recursively). Publishes sites to `ctx.shared` for the
/// server + dto passes. Syntactic (R1/R3, plus constant resolution R8).
class ApiPass implements ReversePass {
  @override
  String get id => 'api';
  @override
  String get outputFile => 'connector/api.json';

  static const _verbs = {'get', 'post', 'put', 'patch', 'delete', 'head'};
  static const _clients = {
    'dio': 'dio', '_dio': 'dio', 'client': 'dio', '_client': 'dio',
    'api': 'dio', '_api': 'dio', 'apiClient': 'dio', '_apiClient': 'dio',
    'http': 'http', '_http': 'http',
  };

  @override
  PassResult run(ScanContext ctx) {
    final units = (ctx.shared['units'] as List).cast<ParsedDart>();

    // Build the cross-file constants table once, then resolve call paths against it.
    final consts = _ConstResolver()..collect(units);
    final calls = <ApiCallSite>[];

    for (final pd in units) {
      final v = _ApiVisitor(pd, consts);
      pd.unit.accept(v);
      calls.addAll(v.calls);
    }

    ctx.shared['apiCalls'] = calls; // consumed by server_pass + dto_pass
    return PassResult(
      'api',
      outputFile,
      {'calls': [for (final c in calls) c.toJson()]},
      itemCount: calls.length,
      notes: ['${consts.count} path constant(s) resolved'],
    );
  }
}

class _ApiVisitor extends RecursiveAstVisitor<void> {
  final ParsedDart pd;
  final _ConstResolver consts;
  final List<ApiCallSite> calls = [];
  _ApiVisitor(this.pd, this.consts);

  @override
  void visitMethodInvocation(MethodInvocation node) {
    final verb = node.methodName.name.toLowerCase();
    final target = node.target;
    if (ApiPass._verbs.contains(verb) && target is SimpleIdentifier) {
      final client = ApiPass._clients[target.name];
      final args = node.argumentList.arguments;
      if (client != null && args.isNotEmpty) {
        final path = _pathTemplate(_positional(args));
        if (path != null && _looksLikePath(path)) {
          calls.add(ApiCallSite(
            method: verb.toUpperCase(),
            pathTemplate: _stripHost(path),
            client: client,
            service: node.thisOrAncestorOfType<ClassDeclaration>()?.name.lexeme,
            serviceMethod: node.thisOrAncestorOfType<MethodDeclaration>()?.name.lexeme,
            provenance: pd.provenanceAt(node.offset, confidence: 'syntactic'),
          ));
        }
      }
    }
    super.visitMethodInvocation(node);
  }

  /// First positional (non-named) argument — the path is never a named arg.
  Expression? _positional(NodeList<Expression> args) {
    for (final a in args) {
      if (a is! NamedExpression) return a;
    }
    return null;
  }

  bool _looksLikePath(String s) => s.startsWith('/') || s.startsWith('http');

  String _stripHost(String s) {
    final m = RegExp(r'^[a-zA-Z][a-zA-Z0-9+.\-]*://[^/]+').firstMatch(s);
    if (m == null) return s;
    final rest = s.substring(m.end);
    return rest.isEmpty ? '/' : rest;
  }

  /// Canonicalize a path argument (literal, interpolation, or constant reference).
  String? _pathTemplate(Expression? e) {
    if (e == null) return null;
    if (e is SimpleStringLiteral) return e.value;
    if (e is AdjacentStrings) {
      final sb = StringBuffer();
      for (final s in e.strings) {
        final r = _pathTemplate(s);
        if (r == null) return null;
        sb.write(r);
      }
      return sb.toString();
    }
    if (e is StringInterpolation) {
      final sb = StringBuffer();
      for (final el in e.elements) {
        if (el is InterpolationString) {
          sb.write(el.value);
        } else if (el is InterpolationExpression) {
          // A constant reference inside interpolation resolves to its value.
          final ref = consts.resolveRef(el.expression);
          sb.write(ref ?? '{${_paramName(el.expression)}}');
        }
      }
      return sb.toString();
    }
    // ApiConstants.services  /  ApiConstants.serviceDetail(id)  → constants table.
    final resolved = consts.resolveRef(e);
    if (resolved != null) return resolved;

    // Uri.parse('...') / Uri.https(...) → recurse into the first string arg.
    if (e is MethodInvocation && e.argumentList.arguments.isNotEmpty) {
      return _pathTemplate(e.argumentList.arguments.first);
    }
    return null;
  }

  String _paramName(Expression e) {
    if (e is SimpleIdentifier) return e.name;
    if (e is PrefixedIdentifier) return e.identifier.name;
    if (e is PropertyAccess) return e.propertyName.name;
    return 'param';
  }
}

/// A resolvable static-string constant or path-building method.
class _ConstDef {
  final Expression expr;
  final Set<String> params; // method params → become {param} in the template
  _ConstDef(this.expr, this.params);
}

/// Project-wide resolver for path constants declared as `static const String x = '...'`
/// or `static String x(id) => '...'`. Interpolation between constants (`$apiVersion/services`)
/// is resolved recursively, with a cycle guard.
class _ConstResolver {
  final Map<String, _ConstDef> _defs = {};
  final Map<String, String?> _memo = {};

  int get count => _memo.values.where((v) => v != null).length;

  void collect(List<ParsedDart> units) {
    for (final pd in units) {
      for (final c in pd.unit.declarations.whereType<ClassDeclaration>()) {
        final cls = c.name.lexeme;
        for (final m in c.members) {
          if (m is FieldDeclaration && m.isStatic) {
            for (final v in m.fields.variables) {
              final init = v.initializer;
              if (init != null) _put(cls, v.name.lexeme, _ConstDef(init, const {}));
            }
          } else if (m is MethodDeclaration && m.isStatic) {
            final body = m.body;
            if (body is ExpressionFunctionBody) {
              final params = <String>{};
              for (final p in m.parameters?.parameters ?? const <FormalParameter>[]) {
                final n = p.name?.lexeme;
                if (n != null) params.add(n);
              }
              _put(cls, m.name.lexeme, _ConstDef(body.expression, params));
            }
          }
        }
      }
    }
    // Warm the memo so `count` reflects resolvable entries.
    for (final k in _defs.keys.toList()) {
      if (!k.contains('.')) _resolveName(k);
    }
  }

  void _put(String cls, String name, _ConstDef def) {
    _defs[name] = def;             // simple name (used by same-class interpolation)
    _defs['$cls.$name'] = def;     // qualified (used by call sites)
  }

  /// Resolve a call-site path expression that references a constant.
  String? resolveRef(Expression e) {
    if (e is PrefixedIdentifier) {
      return _resolveName('${e.prefix.name}.${e.identifier.name}') ?? _resolveName(e.identifier.name);
    }
    if (e is PropertyAccess) {
      final target = e.target;
      final owner = target is Identifier ? target.name : null;
      return (owner != null ? _resolveName('$owner.${e.propertyName.name}') : null) ??
          _resolveName(e.propertyName.name);
    }
    if (e is SimpleIdentifier) return _resolveName(e.name);
    if (e is MethodInvocation) {
      final owner = e.target is Identifier ? (e.target as Identifier).name : null;
      final byQual = owner != null ? _resolveName('$owner.${e.methodName.name}') : null;
      return byQual ?? _resolveName(e.methodName.name);
    }
    return null;
  }

  String? _resolveName(String name) {
    if (_memo.containsKey(name)) return _memo[name];
    final def = _defs[name];
    if (def == null) return null;
    _memo[name] = null; // cycle guard
    final r = _resolveExpr(def.expr, def.params);
    _memo[name] = r;
    return r;
  }

  String? _resolveExpr(Expression e, Set<String> params) {
    if (e is SimpleStringLiteral) return e.value;
    if (e is AdjacentStrings) {
      final sb = StringBuffer();
      for (final s in e.strings) {
        final r = _resolveExpr(s, params);
        if (r == null) return null;
        sb.write(r);
      }
      return sb.toString();
    }
    if (e is StringInterpolation) {
      final sb = StringBuffer();
      for (final el in e.elements) {
        if (el is InterpolationString) {
          sb.write(el.value);
        } else if (el is InterpolationExpression) {
          final nm = _refName(el.expression);
          if (nm != null && params.contains(nm)) {
            sb.write('{$nm}'); // method parameter → path param
          } else {
            final sub = nm != null ? _resolveName(nm) : null;
            sb.write(sub ?? '{${nm ?? 'param'}}');
          }
        }
      }
      return sb.toString();
    }
    return null;
  }

  String? _refName(Expression e) {
    if (e is SimpleIdentifier) return e.name;
    if (e is PrefixedIdentifier) return '${e.prefix.name}.${e.identifier.name}';
    if (e is PropertyAccess) return e.propertyName.name;
    return null;
  }
}
