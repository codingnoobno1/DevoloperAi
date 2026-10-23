import 'package:analyzer/dart/ast/ast.dart';
import 'package:analyzer/dart/ast/visitor.dart';
import '../reverse_config.dart';
import '../parser/dart_source_parser.dart';
import '../parser/provenance.dart';
import '../connector/api_model.dart';
import 'pass.dart';

/// Detects backend targets → `connector/server.json`, and stamps each api call
/// with its `serverId`.
///
/// Signals (ranked): `BaseOptions(baseUrl:)` › const/`kBaseUrl` URL strings.
/// Stack guessed from package imports (graphql/firebase/supabase → else rest).
/// Runs AFTER api_pass (reads/writes `ctx.shared['apiCalls']`).
class ServerPass implements ReversePass {
  @override
  String get id => 'server';
  @override
  String get outputFile => 'connector/server.json';

  @override
  PassResult run(ScanContext ctx) {
    final units = (ctx.shared['units'] as List).cast<ParsedDart>();
    final calls = (ctx.shared['apiCalls'] as List?)?.cast<ApiCallSite>() ?? const [];

    // 1. Collect base-URL candidates + stack signal.
    final candidates = <_UrlHit>[];
    var stack = 'rest';
    for (final pd in units) {
      stack = _stackFromImports(pd) ?? stack;
      final v = _ServerVisitor(pd);
      pd.unit.accept(v);
      candidates.addAll(v.hits);
    }

    // 2. Build one ServerInfo per distinct base URL (fallback: single unknown server if calls exist).
    final servers = <ServerInfo>[];
    final byFile = <String, String>{}; // file → serverId (proximity assignment)
    if (candidates.isEmpty && calls.isNotEmpty) {
      servers.add(ServerInfo(id: 'api_main', baseUrl: null, source: 'cluster', stackGuess: stack, confidence: 'heuristic'));
    } else {
      var i = 0;
      final seen = <String, ServerInfo>{};
      for (final c in candidates) {
        final srv = seen.putIfAbsent(c.url, () {
          final s = ServerInfo(
            id: candidates.length == 1 ? 'api_main' : 'api_${i++}',
            baseUrl: c.url,
            source: c.source,
            stackGuess: stack,
            confidence: 'syntactic',
            provenance: c.provenance,
          );
          servers.add(s);
          return s;
        });
        byFile[c.provenance.file] = srv.id;
      }
    }

    // 3. Stamp each call with a server (same-file first, else the primary).
    final primary = servers.isNotEmpty ? servers.first.id : null;
    for (final call in calls) {
      call.serverId = byFile[call.provenance.file] ?? primary;
      final s = servers.firstWhere((s) => s.id == call.serverId, orElse: () => ServerInfo(id: '_', baseUrl: null, source: ''));
      if (s.id != '_') s.endpointsCount++;
    }

    return PassResult(
      'server',
      outputFile,
      {
        'servers': [for (final s in servers) s.toJson()],
        'unresolvedBaseUrls': [
          for (final c in calls.where((c) => c.serverId == null))
            {'endpoint': c.pathTemplate, 'provenance': c.provenance.toJson()}
        ],
      },
      itemCount: servers.length,
      notes: ['stack: $stack'],
      // Re-emit api.json now that every call carries its serverId.
      extraOutputs: {
        'connector/api.json': {'calls': [for (final c in calls) c.toJson()]},
      },
    );
  }

  String? _stackFromImports(ParsedDart pd) {
    for (final d in pd.unit.directives.whereType<ImportDirective>()) {
      final uri = d.uri.stringValue ?? '';
      if (uri.contains('graphql')) return 'graphql';
      if (uri.contains('firebase') || uri.contains('cloud_firestore')) return 'firebase';
      if (uri.contains('supabase')) return 'supabase';
      if (uri.contains('grpc')) return 'grpc';
    }
    return null;
  }
}

class _UrlHit {
  final String url;
  final String source; // BaseOptions | const
  final Provenance provenance;
  _UrlHit(this.url, this.source, this.provenance);
}

class _ServerVisitor extends RecursiveAstVisitor<void> {
  final ParsedDart pd;
  final List<_UrlHit> hits = [];
  _ServerVisitor(this.pd);

  @override
  void visitMethodInvocation(MethodInvocation node) {
    // BaseOptions(baseUrl: '...') — non-const, parses as a MethodInvocation.
    if (node.methodName.name == 'BaseOptions') {
      for (final arg in node.argumentList.arguments.whereType<NamedExpression>()) {
        if (arg.name.label.name == 'baseUrl') {
          final url = _stringOf(arg.expression);
          if (url != null) hits.add(_UrlHit(url, 'BaseOptions', pd.provenanceAt(node.offset)));
        }
      }
    }
    super.visitMethodInvocation(node);
  }

  @override
  void visitVariableDeclaration(VariableDeclaration node) {
    // const kBaseUrl = 'https://...';  (name hints + http value)
    final name = node.name.lexeme.toLowerCase();
    if (name.contains('url') || name.contains('base') || name.contains('api') || name.contains('endpoint')) {
      final url = node.initializer is Expression ? _stringOf(node.initializer!) : null;
      if (url != null && url.startsWith('http')) {
        hits.add(_UrlHit(url, 'const', pd.provenanceAt(node.offset)));
      }
    }
    super.visitVariableDeclaration(node);
  }

  String? _stringOf(Expression e) => e is SimpleStringLiteral ? e.value : null;
}
