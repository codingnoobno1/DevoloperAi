import '../reverse_config.dart';
import 'pass.dart';

/// Extracts DTOs → `dto.json`: the subset of models that cross the wire
/// (serializable via fromJson/toJson). Direction (request/response) is guessed
/// from naming. Runs AFTER models_pass (reads `ctx.shared['models']`).
///
/// Deeper endpoint↔DTO binding (which call decodes into which DTO) needs the
/// resolved element model and lands in R5; here we surface the wire types.
class DtoPass implements ReversePass {
  @override
  String get id => 'dto';
  @override
  String get outputFile => 'dto.json';

  static final _request = RegExp(r'(Request|Query|Payload|Input|Params)$');
  static final _response = RegExp(r'(Response|Result|Dto|DTO|List)$');

  @override
  PassResult run(ScanContext ctx) {
    final models = (ctx.shared['models'] as List?)?.cast<Map<String, dynamic>>() ?? const [];

    final dtos = <Map<String, dynamic>>[];
    for (final m in models) {
      if (m['serializable'] != true) continue;
      final name = m['name'] as String;
      dtos.add({
        'name': name,
        'direction': _request.hasMatch(name)
            ? 'request'
            : _response.hasMatch(name)
                ? 'response'
                : 'unknown',
        'fields': m['fields'],
        'provenance': m['provenance'],
      });
    }

    dtos.sort((a, b) => (a['name'] as String).compareTo(b['name'] as String));
    return PassResult('dto', outputFile, {'dtos': dtos}, itemCount: dtos.length,
        notes: ['serializable models; endpoint binding deferred to R5']);
  }
}
