import 'package:analyzer/dart/ast/ast.dart';
import '../reverse_config.dart';
import '../parser/dart_source_parser.dart';
import '../connector/api_model.dart';
import 'pass.dart';

/// Catalogs API-client / repository classes → `service.json`, enriched with the
/// endpoints each method calls (grouped from the api pass). Runs AFTER api_pass.
class ServicesPass implements ReversePass {
  @override
  String get id => 'services';
  @override
  String get outputFile => 'service.json';

  static final _serviceName = RegExp(r'(Service|Repository|Api|Client|Gateway|DataSource|Datasource)$');
  static const _clientTypes = {'Dio': 'dio', 'Client': 'http', 'HttpClient': 'http'};

  @override
  PassResult run(ScanContext ctx) {
    final units = (ctx.shared['units'] as List).cast<ParsedDart>();
    final calls = (ctx.shared['apiCalls'] as List?)?.cast<ApiCallSite>() ?? const [];

    // Group resolved calls by enclosing service.
    final callsByService = <String, List<ApiCallSite>>{};
    for (final c in calls) {
      if (c.service != null) callsByService.putIfAbsent(c.service!, () => []).add(c);
    }

    final services = <Map<String, dynamic>>[];
    for (final pd in units) {
      for (final c in pd.unit.declarations.whereType<ClassDeclaration>()) {
        final name = c.name.lexeme;

        // Client field type (Dio/http.Client).
        String? client;
        for (final f in c.members.whereType<FieldDeclaration>()) {
          final t = f.fields.type?.toSource();
          if (t != null && _clientTypes.containsKey(t)) client = _clientTypes[t];
        }

        final grouped = callsByService[name] ?? const [];
        final isService = client != null || _serviceName.hasMatch(name) || grouped.isNotEmpty;
        if (!isService) continue;

        // Methods that issue calls.
        final methods = <Map<String, dynamic>>[];
        for (final m in c.members.whereType<MethodDeclaration>()) {
          final mCalls = grouped.where((x) => x.serviceMethod == m.name.lexeme).toList();
          if (mCalls.isEmpty) continue;
          methods.add({
            'name': m.name.lexeme,
            'returnType': m.returnType?.toSource(),
            'calls': [for (final x in mCalls) {'method': x.method, 'endpoint': x.pathTemplate}],
          });
        }

        services.add({
          'name': name,
          'kind': name.endsWith('Repository') ? 'repository' : 'service',
          if (client != null) 'client': client,
          'server': grouped.isNotEmpty ? grouped.first.serverId : null,
          'endpointsCount': grouped.length,
          'methods': methods,
          'provenance': pd.provenanceAt(c.offset).toJson(),
        });
      }
    }

    services.sort((a, b) => (a['name'] as String).compareTo(b['name'] as String));
    return PassResult('services', outputFile, {'services': services}, itemCount: services.length);
  }
}
