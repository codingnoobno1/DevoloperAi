import 'package:analyzer/dart/ast/ast.dart';
import '../reverse_config.dart';
import '../parser/dart_source_parser.dart';
import '../parser/ast_helpers.dart';
import 'pass.dart';

/// Extracts plain data classes → `model.json`.
///
/// Heuristic (syntactic, R1): a data model is a class that is NOT a widget and
/// declares instance fields; `fromJson`/`toJson` presence marks it serializable
/// (a strong DTO signal the R3 dto_pass consumes via `ctx.shared['models']`).
class ModelsPass implements ReversePass {
  @override
  String get id => 'models';
  @override
  String get outputFile => 'model.json';

  static const _nonModelBases = {
    'StatelessWidget', 'StatefulWidget', 'State', 'ChangeNotifier',
    'Cubit', 'Bloc', 'InheritedWidget', 'Widget',
  };

  // Behavioural classes (services, repos, blocs, controllers) are not data models.
  static final _nonModelName = RegExp(
      r'(Service|Repository|Controller|Provider|Manager|Bloc|Cubit|Client|Api|DataSource|Datasource|Handler|Delegate|Widget|Screen|Page|View)$');

  // A field of one of these types marks the class as behavioural, not data.
  static const _clientFieldTypes = {'Dio', 'Client', 'HttpClient', 'http.Client'};

  @override
  PassResult run(ScanContext ctx) {
    final units = (ctx.shared['units'] as List).cast<ParsedDart>();
    final models = <Map<String, dynamic>>[];

    for (final pd in units) {
      for (final c in pd.unit.declarations.whereType<ClassDeclaration>()) {
        final base = superclassName(c);
        if (base != null && _nonModelBases.contains(base)) continue;
        if (_nonModelName.hasMatch(c.name.lexeme)) continue;

        final fields = <Map<String, dynamic>>[];
        var hasClientField = false;
        for (final m in c.members.whereType<FieldDeclaration>()) {
          if (m.isStatic) continue;
          final typeName = m.fields.type?.toSource() ?? 'dynamic';
          if (_clientFieldTypes.contains(typeName)) hasClientField = true;
          for (final v in m.fields.variables) {
            fields.add({'name': v.name.lexeme, 'type': typeName});
          }
        }
        if (fields.isEmpty || hasClientField) continue; // no data / behavioural → not a model

        final methods = c.members.whereType<MethodDeclaration>().map((m) => m.name.lexeme).toSet();
        final serializable = methods.contains('fromJson') || methods.contains('toJson');

        models.add({
          'name': c.name.lexeme,
          'fields': fields,
          'serializable': serializable,
          'provenance': pd.provenanceAt(c.offset).toJson(),
        });
      }
    }

    models.sort((a, b) => (a['name'] as String).compareTo(b['name'] as String));
    // Publish for the DTO pass (R3).
    ctx.shared['models'] = models;
    return PassResult('models', outputFile, {'models': models}, itemCount: models.length);
  }
}
