import 'package:analyzer/dart/ast/ast.dart';
import '../reverse_config.dart';
import '../parser/dart_source_parser.dart';
import 'pass.dart';

/// Extracts local/remote data sources → `datasource.json`, with the persistent
/// database subset mirrored into `db.json`. Detected from package imports and
/// construction calls. Syntactic (R4).
class DatasourcePass implements ReversePass {
  @override
  String get id => 'datasource';
  @override
  String get outputFile => 'datasource.json';

  // package fragment → (kind, isDatabase)
  static const _packages = <String, List<Object>>{
    'hive': ['hive', true],
    'sqflite': ['sqflite', true],
    'isar': ['isar', true],
    'drift': ['drift', true],
    'moor': ['drift', true],
    'objectbox': ['objectbox', true],
    'shared_preferences': ['shared_preferences', false],
    'flutter_secure_storage': ['secure_storage', false],
  };

  @override
  PassResult run(ScanContext ctx) {
    final units = (ctx.shared['units'] as List).cast<ParsedDart>();
    final sources = <Map<String, dynamic>>[];
    final databases = <Map<String, dynamic>>[];

    for (final pd in units) {
      final found = <String>{};
      for (final d in pd.unit.directives.whereType<ImportDirective>()) {
        final uri = d.uri.stringValue ?? '';
        for (final entry in _packages.entries) {
          if (uri.contains(entry.key) && found.add(entry.value[0] as String)) {
            final kind = entry.value[0] as String;
            final isDb = entry.value[1] as bool;
            final rec = {
              'kind': kind,
              'scope': isDb ? 'database' : 'key-value',
              'evidence': 'import package:${entry.key}',
              'provenance': pd.provenanceAt(d.offset).toJson(),
            };
            sources.add(rec);
            if (isDb) databases.add(rec);
          }
        }
      }
    }

    return PassResult(
      'datasource',
      outputFile,
      {'sources': sources},
      itemCount: sources.length,
      extraOutputs: {
        'db.json': {'databases': databases},
      },
      notes: ['${databases.length} persistent database(s)'],
    );
  }
}
